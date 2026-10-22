import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('capture_registration', ROOT/'scripts/hub-registration/register_capture.py')
m = importlib.util.module_from_spec(spec); sys.modules[spec.name]=m; spec.loader.exec_module(m)

class RegistrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root/'config').mkdir(); (self.root/'mcps').mkdir()
        self.env = self.root/'config/runtime.env'; self.env.write_text('ORIGINAL=keep\n'); self.env.chmod(0o600)
        self.token = self.root/'token'; self.token.write_text('a'*54); self.token.chmod(0o600)
        self.catalog = self.root/'catalog.json'; self.catalog.write_bytes((ROOT/'config/hub-capture.example.json').read_bytes())
        self.current = {'Id':'original', 'Image':'sha256:original', 'State':{'Running':True},
            'Mounts':[{'Type':'bind','Source':str(self.root/'mcps'),'Destination':'/app/config/mcps'}],
            'Config':{'Image':'sha256:original', 'Env':['FLAMORIS_MCP_HUB_CLIENT_TOKEN='+'b'*40,'OTHER=keep'],
                'User':'10001:10001','Labels':{'flamoris.updater.operation':'existing'},'Cmd':['unchanged']},
            'HostConfig':{'NetworkMode':'host','ReadonlyRootfs':True,'CapDrop':['ALL'],
                'Memory':123456,'PidsLimit':100,'RestartPolicy':{'Name':'unless-stopped'},
                'Mounts':[{'Type':'bind','Source':str(self.root/'mcps'),'Target':'/app/config/mcps','ReadOnly':True}]}}
        self.args = type('Args', (), {'root':str(self.root),'catalog':str(self.catalog),'token_file':str(self.token)})()
        self.calls = []
    def docker(self, method, path, body=None):
        self.calls.append((method,path,copy.deepcopy(body)))
        if method=='GET': return copy.deepcopy(self.current)
        return {}
    def test_clone_preserves_all_runtime_security_and_other_env(self):
        before = copy.deepcopy(self.current)
        result = m.build_spec(self.current,'a'*54)
        self.assertEqual(before,self.current)
        self.assertEqual(self.current['HostConfig'],result.pop('HostConfig'))
        self.assertEqual(self.current['Config']['Env']+[m.TOKEN_KEY+'='+'a'*54],result['Env'])
        result['Env'] = self.current['Config']['Env']
        self.assertEqual(self.current['Config'],result)
    def test_unverified_network_is_refused(self):
        self.current['HostConfig']['NetworkMode']='bridge'
        with self.assertRaisesRegex(m.Failure,'unverified_network'): m.build_spec(self.current,'a'*54)
    def test_existing_connector_is_not_rotated(self):
        self.current['Config']['Env'].append(m.TOKEN_KEY+'=old')
        with self.assertRaisesRegex(m.Failure,'already_in_container'): m.build_spec(self.current,'new')
    def test_catalog_has_exact_capture_contract_and_no_secret(self):
        value = json.loads(self.catalog.read_text()); m.validate_catalog(value)
        for tool in value['tools']:
            self.assertEqual({'readOnlyHint':True,'destructiveHint':False,'openWorldHint':False},tool['annotations'])
        value['tools'].append(value['tools'][0])
        with self.assertRaises(m.Failure): m.validate_catalog(value)
    def test_success_preserves_existing_files_and_updates_only_capture(self):
        existing=self.root/'mcps/cutwork.yaml'; existing.write_text('untouched')
        before={'cutwork':9,'generation':25}; after={**before,'capture':3}
        with patch.object(m.os,'geteuid',return_value=0), patch.object(m,'docker',side_effect=self.docker), patch.object(m,'rpc_status',side_effect=[before,after]), patch.object(m.os,'fchown'):
            result=m.install(self.args)
        self.assertEqual('registered',result['status']); self.assertEqual('untouched',existing.read_text())
        self.assertEqual('ORIGINAL=keep\nCAPTURE_CONNECTOR_TOKEN='+'a'*54+'\n',self.env.read_text())
        self.assertTrue(any(path.startswith('/containers/create?') for _,path,_ in self.calls))
        self.assertTrue(any(method=='DELETE' and 'rollback-' in path for method,path,_ in self.calls))
        self.assertNotIn('a'*54,json.dumps(result))
    def test_failed_creation_restores_old_env_catalog_and_container(self):
        def fail_create(method,path,body=None):
            if path.startswith('/containers/create'): raise m.Failure('simulated')
            return self.docker(method,path,body)
        with patch.object(m.os,'geteuid',return_value=0), patch.object(m,'docker',side_effect=fail_create), patch.object(m,'rpc_status',return_value={'cutwork':9}), patch.object(m.os,'fchown'):
            with self.assertRaisesRegex(m.Failure,'original_restored'): m.install(self.args)
        self.assertEqual('ORIGINAL=keep\n',self.env.read_text())
        self.assertFalse((self.root/'mcps/capture.yaml').exists())
        self.assertTrue(any('rename?name=flamoris-mcp-hub' in path for _,path,_ in self.calls))
    def test_health_failure_removes_new_container_and_restores_original(self):
        with patch.object(m.os,'geteuid',return_value=0), patch.object(m,'docker',side_effect=self.docker), patch.object(m,'rpc_status',return_value={'cutwork':9}), patch.object(m.time,'sleep'), patch.object(m.os,'fchown'):
            with self.assertRaisesRegex(m.Failure,'original_restored'): m.install(self.args)
        self.assertIn(('DELETE','/containers/flamoris-mcp-hub?force=true',None),self.calls)
        self.assertEqual('ORIGINAL=keep\n',self.env.read_text())
    def test_existing_registration_is_idempotent(self):
        (self.root/'mcps/capture.yaml').write_bytes(self.catalog.read_bytes())
        self.env.write_text('ORIGINAL=keep\nCAPTURE_CONNECTOR_TOKEN='+'a'*54+'\n')
        self.current['Config']['Env'].append(m.TOKEN_KEY+'='+'a'*54)
        with patch.object(m.os,'geteuid',return_value=0), patch.object(m,'docker',side_effect=self.docker), patch.object(m,'rpc_status',return_value={'capture':3}):
            result=m.install(self.args)
        self.assertEqual('already_registered',result['status'])
        self.assertTrue(all(method=='GET' for method,_,_ in self.calls))
    def test_world_readable_token_is_rejected_before_docker(self):
        self.token.chmod(0o644)
        with patch.object(m.os,'geteuid',return_value=0), patch.object(m,'docker') as docker:
            with self.assertRaisesRegex(m.Failure,'credential_file_permissions'): m.install(self.args)
            docker.assert_not_called()

if __name__=='__main__': unittest.main()
