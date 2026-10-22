#!/usr/bin/env python3
"""Explicit host-side catalog/credential rollout; Python stdlib only, no screen access.
Run through the companion PowerShell launcher. Never print inspect/env contents.
"""
from __future__ import annotations
import argparse
import copy
import http.client
import json
import os
from pathlib import Path
import re
import socket
import sys
import tempfile
import time
import urllib.request
import uuid

NAME = 'flamoris-mcp-hub'
TOKEN_KEY = 'CAPTURE_CONNECTOR_TOKEN'
TOOLS = {'mcp.context', 'capture.session.get', 'capture.screenshot.get'}

class Failure(Exception):
    pass

class DockerConnection(http.client.HTTPConnection):
    def __init__(self):
        super().__init__('localhost', timeout=45)
    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect('/var/run/docker.sock')

def docker(method, path, body=None):
    connection = DockerConnection()
    try:
        data = None if body is None else json.dumps(body).encode()
        connection.request(method, path, data, {'Content-Type': 'application/json'})
        response = connection.getresponse()
        raw = response.read(2 * 1024 * 1024 + 1)
        if len(raw) > 2 * 1024 * 1024 or response.status >= 400:
            raise Failure('docker_operation_failed')
        return json.loads(raw) if raw else None
    finally:
        connection.close()

def read_regular(path, private=False):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, 'rb') as stream:
        st = os.fstat(stream.fileno())
        import stat
        if not stat.S_ISREG(st.st_mode) or st.st_size > 1024 * 1024:
            raise Failure('invalid_configuration_file')
        if private and st.st_mode & 0o077:
            raise Failure('credential_file_permissions')
        return stream.read(), st

def atomic(path, data, mode, uid, gid):
    fd, temporary = tempfile.mkstemp(prefix='.capture-', dir=path.parent)
    try:
        os.fchmod(fd, mode); os.fchown(fd, uid, gid)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary): os.unlink(temporary)

def build_spec(current, token):
    if current['HostConfig'].get('NetworkMode') != 'host':
        raise Failure('unverified_network_layout')
    spec = copy.deepcopy(current['Config'])
    environment = spec.get('Env', [])
    if any(x.startswith(TOKEN_KEY + '=') for x in environment):
        raise Failure('connector_already_in_container')
    spec['Env'] = environment + [TOKEN_KEY + '=' + token]
    spec['HostConfig'] = copy.deepcopy(current['HostConfig'])
    # Config.Image, labels, command, user, mounts, limits and security flags remain identical.
    return spec

def validate_catalog(catalog):
    if (catalog.get('id') != 'capture' or catalog.get('namespace') != 'capture'
        or catalog.get('transport') != 'reverse-websocket' or catalog.get('product_id') != 'flamoris.capture'
        or catalog.get('connector_token_env') != TOKEN_KEY
        or {x['name'] for x in catalog.get('tools', [])} != TOOLS
        or len(catalog['tools']) != 3):
        raise Failure('catalog_contract_mismatch')

def rpc_status(url, token):
    headers = {'Authorization': 'Bearer ' + token, 'Accept': 'application/json, text/event-stream', 'Content-Type': 'application/json'}
    def post(body):
        request = urllib.request.Request(url, data=json.dumps(body).encode(), headers=headers)
        with urllib.request.urlopen(request, timeout=5) as response:
            if response.headers.get('Mcp-Session-Id'): headers['Mcp-Session-Id'] = response.headers['Mcp-Session-Id']
            raw = response.read(2 * 1024 * 1024 + 1)
            if len(raw) > 2 * 1024 * 1024: raise Failure('response_limit')
            if not raw: return None
            if 'text/event-stream' in response.headers.get('Content-Type', ''):
                values = [line[5:].strip() for line in raw.decode().splitlines() if line.startswith('data:')]
                return json.loads(values[-1])
            return json.loads(raw)
    try:
        result = post({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-03-26','capabilities':{},'clientInfo':{'name':'capture-registration','version':'1'}}})
        if not result or 'error' in result: raise Failure('hub_initialize_failed')
        post({'jsonrpc':'2.0','method':'notifications/initialized'})
        reply = post({'jsonrpc':'2.0','id':2,'method':'tools/call','params':{'name':'hub.upstreams.list','arguments':{}}})
        if not reply or 'error' in reply or reply['result'].get('isError'): raise Failure('hub_status_failed')
        result = reply['result']
        value = result.get('structuredContent')
        if not value:
            value = json.loads(next(x['text'] for x in result['content'] if x['type']=='text'))
        return {x['id']: x['configured_tools'] for x in value['upstreams']}
    finally:
        if 'Mcp-Session-Id' in headers:
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers=headers, method='DELETE'), timeout=3): pass
            except Exception: pass

def install(args):
    if os.geteuid() != 0: raise Failure('run_with_sudo')
    root = Path(args.root).resolve()
    env_path, target = root/'config/runtime.env', root/'mcps/capture.yaml'
    catalog_raw, _ = read_regular(Path(args.catalog))
    catalog = json.loads(catalog_raw); validate_catalog(catalog)
    token_raw, _ = read_regular(Path(args.token_file), private=True)
    token = token_raw.decode().strip()
    if not re.fullmatch(r'[A-Za-z0-9_-]{40,128}', token): raise Failure('invalid_token')
    env_raw, env_stat = read_regular(env_path, private=True)
    env_text = env_raw.decode('utf-8')
    current = docker('GET', '/containers/' + NAME + '/json')
    if not current['State']['Running']: raise Failure('hub_not_running')
    if not any(m['Type']=='bind' and m['Source']==str(root/'mcps') and m['Destination']=='/app/config/mcps' for m in current['Mounts']):
        raise Failure('layout_changed')
    env = dict(x.split('=',1) for x in current['Config']['Env'])
    client_token = env.get('FLAMORIS_MCP_HUB_CLIENT_TOKEN')
    if not client_token: raise Failure('client_token_unavailable')
    url = 'http://127.0.0.1:' + str(int(env.get('FLAMORIS_MCP_HUB_PORT','8769'))) + '/mcp'
    before = rpc_status(url, client_token)
    if target.is_symlink(): raise Failure('catalog_symlink')
    if target.exists() or re.search(r'^\s*(?:export\s+)?' + TOKEN_KEY + r'\s*=', env_text, re.M):
        if target.exists() and json.loads(read_regular(target)[0]) == catalog and 'capture' in before and before['capture']==3 and env.get(TOKEN_KEY):
            return {'status':'already_registered','upstreams':before,'credential_changed':False}
        raise Failure('existing_capture_requires_review')
    if 'capture' in before: raise Failure('capture_namespace_exists')
    spec = build_spec(current, token)
    backup = root/'config'/('capture-backup-' + time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
    backup.mkdir(mode=0o700)
    atomic(backup/'runtime.env', env_raw, 0o600, 0, 0)
    atomic(backup/'container.json', json.dumps(current).encode(), 0o600, 0, 0)
    # The managed Docker env-file is read at container creation, so restart alone is insufficient.
    # Recreate the same container with its complete inspected Config/HostConfig and one added env key.
    previous = NAME + '-capture-rollback-' + uuid.uuid4().hex[:10]
    renamed = created = False
    try:
        atomic(target, catalog_raw, 0o644, 10001, 10001)
        changed = env_text.rstrip('\n') + '\n' + TOKEN_KEY + '=' + token + '\n'
        atomic(env_path, changed.encode(), env_stat.st_mode & 0o777, env_stat.st_uid, env_stat.st_gid)
        docker('POST', '/containers/' + NAME + '/stop?t=15')
        docker('POST', '/containers/' + NAME + '/rename?name=' + previous); renamed = True
        docker('POST', '/containers/create?name=' + NAME, spec); created = True
        docker('POST', '/containers/' + NAME + '/start')
        expected = {**before, 'capture':3}
        for _ in range(30):
            try:
                if rpc_status(url, client_token) == expected: break
            except Exception: pass
            time.sleep(1)
        else: raise Failure('health_verification_failed')
        if docker('GET', '/containers/' + NAME + '/json')['Image'] != current['Image']:
            raise Failure('image_changed')
    except BaseException:
        # Restore config and the original Docker object on failure. No other upstream files are edited.
        atomic(env_path, env_raw, env_stat.st_mode & 0o777, env_stat.st_uid, env_stat.st_gid)
        if target.exists(): target.unlink()
        if created:
            docker('DELETE', '/containers/' + NAME + '?force=true')
        if renamed: docker('POST', '/containers/' + previous + '/rename?name=' + NAME)
        docker('POST', '/containers/' + NAME + '/start')
        raise Failure('registration_failed_original_restored')
    # No second managed runtime remains. Root-only config/inspect backups retain rollback information.
    docker('DELETE', '/containers/' + previous)
    return {'status':'registered','upstreams':expected,'credential_changed':True,'image_preserved':True,
            'capture_connected':False,'note':'Desktop connection is the next explicit human step.'}

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--root', required=True); p.add_argument('--catalog', required=True); p.add_argument('--token-file', required=True)
    args = p.parse_args()
    try:
        result = install(args)
        print(json.dumps(result)); return 3 if result['status']=='already_registered' else 0
    except Exception as error:
        code = str(error) if isinstance(error, Failure) else type(error).__name__
        print(json.dumps({'status':'error','code':code})); return 1
if __name__ == '__main__': sys.exit(main())
