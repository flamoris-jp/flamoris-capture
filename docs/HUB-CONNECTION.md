# Connecting Capture to MCP Hub

Capture remains the authority for target selection, consent and pixels. The Hub only pins and forwards the seven-tool wire catalog; it does not start capture or select a target.

## Catalog

`config/hub-capture.example.json` is generated from the actual Capture adapter and MCP Core 1.4.0 `DescribeCatalog()` output. JSON is also valid YAML. `HubCatalogTests` checks the pinned schemas and descriptions against the real producer on every build. Read-only annotations are added explicitly.

Copy the reviewed file into the deployment's MCP catalog directory as `capture.yaml`. Its identity is `capture`, product `flamoris.capture`, transport `reverse-websocket`, credential reference `CAPTURE_CONNECTOR_TOKEN`. The actual secret must stay outside the catalog/source control.

The existing local tool contract includes `capture.` in its name. Hub namespacing therefore currently advertises:

| Public Hub tool | Capture local tool |
|---|---|
| `capture.mcp.context` | `mcp.context` |
| `capture.capture.session.get` | `capture.session.get` |
| `capture.capture.screenshot.get` | `capture.screenshot.get` |
| `capture.capture.recording.get` | `capture.recording.get` |
| `capture.capture.frame.get` | `capture.frame.get` |
| `capture.capture.contactSheet.get` | `capture.contactSheet.get` |
| `capture.capture.events.get` | `capture.events.get` |

The repeated namespace is intentional compatibility with the already running 0.1.0 preview. It is not an extra capture authority.

## Explicit deployment helper

The optional `scripts/hub-registration/connect-capture.ps1` launcher runs from the human's Windows PC using that person's SSH access. It does not give ChatGPT shell or sudo access. Use the actual Hub application root from Updater's recorded layout, not an old Git checkout or another product's path:

```powershell
.\connect-capture.ps1 -SshTarget user@hub-host -HubRoot /actual/managed/hub/root -HubEndpoint wss://your-hub/desktop/capture
```

The downloadable task bundle can include a non-secret `deployment.json` with `sshTarget` and `hubRoot`, so those parameters are optional. The script can derive the WSS URL from existing shared desktop `HubEndpoint` preferences under the user's FLAMORIS LocalAppData folder. It reads only that non-secret preference. If ambiguous/absent, paste the WSS URL from the connected Cutwork/Kachinco settings; the suffix is changed to `/desktop/capture`.

The launcher generates a dedicated 320-bit random Capture connector credential on the local PC, transfers it through a private temporary SSH directory, and deletes staging files afterward. The credential is not printed or embedded in this bundle. On success it is copied to the local clipboard for the human to paste into Capture's credential field. It does not reuse any other desktop's token or the Hub front-door token.

The host helper:

1. Verifies the current running Hub/container mount layout and authenticated `hub.upstreams.list`, and refuses conflicting Capture state.
2. Backs up runtime.env and the container's complete inspected configuration to a root-only directory under the Hub's config folder.
3. Adds only `capture.yaml` and one dedicated environment key. Existing catalogs, image, command, labels, user, mounts, resource/security settings and other credentials are preserved.
4. Recreates only the Hub container from its full inspected Config/HostConfig. Docker `--env-file` values are fixed at container creation, so a simple restart would not load a newly added secret. The supported helper path currently verifies **host networking**, matching the selected recorded deployment; it refuses an unverified network layout.
5. Verifies the same image and exactly the previous upstream tool counts plus Capture's seven tools. On failure it restores files and the original Docker container. On success it removes the stopped old container; root-only configuration backups remain.

Do not run an Updater change concurrently with this explicit configuration operation. Updater remains the installation/image owner; its stable container name and runtime.env location are retained. Existing desktop sockets disconnect during Hub recreation and need reconnecting. No other service/container is restarted. Running the helper again does not silently rotate an existing Capture credential.

These deployment paths are source-reviewed and tested with mocks, not automatically executed by repository CI. Live registration is confirmed only after the human runs the helper and `hub.upstreams.list` shows Capture.

## Reverse proxy route (independent of Hub registration)

A registered Capture catalog/credential does **not** create a public WebSocket route. Verify the actual proxy configuration before desktop connection. In deployments that use exact Nginx locations for each product, `/desktop/capture` needs the same upstream and WebSocket upgrade headers as a working desktop route. A location for `/desktop/cutwork` alone does not cover Capture. Do not change the endpoint to another product's path.

For an existing Nginx installation with exactly one simple `/desktop/cutwork` location proxying directly to `http://127.0.0.1:8769`, the optional repair is:

```bash
sudo python3 scripts/hub-registration/add_capture_route.py
```

Run this on the **proxy host** in the human's SSH terminal (including PuTTY). The helper inspects `nginx -T` locally without printing the dump, finds the actually loaded source file, copies the entire exact Cutwork location into the same server with only its path changed, saves a private backup under `/var/backups`, runs `nginx -t`, then reloads Nginx. Syntax/reload failure restores the original and reloads it. Ambiguous, nested, different-port or already configured layouts are refused for review. This is a separate explicit operation; the registration launcher does not modify Nginx. Other reverse proxies require their own verified configuration.

After a successful reload, retry **MCP / AI → 接続** in Capture. Do not rerun registration or rotate its credential for a missing route. Confirm the live authenticated desktop connection separately; a configuration test alone does not prove it.

## Read-only connection troubleshooting

Capture currently accepts only Read only. The shared connection dialog also offers Edit for other products; selecting Edit in Capture is rejected locally **before any Hub connection**. Set Read only in settings and again in the connection confirmation. An existing credential entry does not override the selected permission. Updated Capture previews explain this explicitly.

Turning sharing OFF invalidates old content authority while preserving the MCP connection, and live acquisition/recording stops immediately; the live preview is cleared. Operation recording retains its independent consent. Selecting another target, stopping, target loss or OS lock clears it.

## Human desktop steps after registration

1. In Capture, open **MCP / AI → 設定…**.
2. Choose **FLAMORIS MCP Hub**, **読み取り専用**, and the WSS endpoint printed by the launcher.
3. Paste the clipboard credential into the password field and Save. The shared subsystem stores it in the current user's Windows Credential Manager. Do not paste it into chat/Issues/screenshots.
4. Use **MCP / AI → 接続… → 読み取り専用 → 接続** before selecting a target or enabling sharing. Enable **起動時に自動接続** to reuse these provider settings on restart; saved OFF remains OFF. Confirm `mcp.context` / safe session status with sharing OFF.
5. Select/allow the desired window and explicitly enable **画面共有** (or **操作記録** for event-only observation). Connection alone grants neither.
6. Confirm the green connection indicator. Refresh ChatGPT's Hub tool catalog if needed; call `capture.capture.screenshot.get` with `{"input":{}}`. No session ID lookup is needed.
7. Confirm a real MCP image is displayed, then test sharing revoke and old-session refusal. Registration/green connection alone does not prove physical-image disclosure or revoke acceptance.

Capture does not resume sharing after restart, target replacement or OS lock. Registration does not change that consent policy. Recording and opt-in target-filtered input events are available; monitor/rectangle targets remain unavailable.

## Upgrade an already registered 0.1 catalog

The 0.2 app alone cannot update a statically pinned Hub catalog. From an updated checkout **on the Hub host**, use the actual managed installation root:

```bash
sudo python3 scripts/hub-registration/register_capture.py --update-catalog \
  --root /actual/managed/hub/root --catalog config/hub-capture.example.json
```

This path accepts the recognized old/new Capture catalog identity, preserves the existing connector credential, runtime.env, container ID/image/settings and all other catalogs, saves a private catalog backup, and restarts only the existing Hub container to reload tools. It checks authenticated upstream counts and restores the old catalog/restarts on failure. It never recreates the container or rotates a token. Active desktop clients need reconnecting; refresh the ChatGPT plugin's tool catalog after Hub restart. Do not rerun the first-time credential launcher for this upgrade. This helper has mocked tests; it is not executed on a production host by CI.
