# Connecting the screenshot preview to MCP Hub

Capture remains the authority for target selection, consent and pixels. The Hub only pins and forwards the existing three-tool wire catalog; it does not start capture or select a target.

## Catalog

`config/hub-capture.example.json` is generated from the actual Capture adapter and MCP Core 1.3.1 `DescribeCatalog()` output. JSON is also valid YAML. `HubCatalogTests` checks the pinned schemas and descriptions against the real producer on every build. Read-only annotations are added explicitly.

Copy the reviewed file into the deployment's MCP catalog directory as `capture.yaml`. Its identity is `capture`, product `flamoris.capture`, transport `reverse-websocket`, credential reference `CAPTURE_CONNECTOR_TOKEN`. The actual secret must stay outside the catalog/source control.

The existing local tool contract includes `capture.` in its name. Hub namespacing therefore currently advertises:

| Public Hub tool | Capture local tool |
|---|---|
| `capture.mcp.context` | `mcp.context` |
| `capture.capture.session.get` | `capture.session.get` |
| `capture.capture.screenshot.get` | `capture.screenshot.get` |

The repeated namespace is intentional compatibility with the already running 0.1.0 preview. It is not an extra capture authority or an implemented video tool.

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
5. Verifies the same image and exactly the previous upstream tool counts plus Capture's three tools. On failure it restores files and the original Docker container. On success it removes the stopped old container; root-only configuration backups remain.

Do not run an Updater change concurrently with this explicit configuration operation. Updater remains the installation/image owner; its stable container name and runtime.env location are retained. Existing desktop sockets disconnect during Hub recreation and need reconnecting. No other service/container is restarted. Running the helper again does not silently rotate an existing Capture credential.

These deployment paths are source-reviewed and tested with mocks, not automatically executed by repository CI. Live registration is confirmed only after the human runs the helper and `hub.upstreams.list` shows Capture.

## Human desktop steps after registration

1. In Capture, open **MCP / AI → 設定…**.
2. Choose **FLAMORIS MCP Hub**, **読み取り専用**, and the WSS endpoint printed by the launcher.
3. Paste the clipboard credential into the password field and Save. The shared subsystem stores it in the current user's Windows Credential Manager. Do not paste it into chat/Issues/screenshots.
4. Select/allow the desired window, check **この対象の画像をMCPで共有する**, then **MCP / AI → 接続… → 読み取り専用 → 接続**.
5. Confirm the green connection indicator. Refresh ChatGPT's Hub tool catalog if needed; call `capture.mcp.context` first and use `documentToken` as the screenshot `input.sessionId`.
6. Confirm a real MCP image is displayed, then test sharing revoke and old-session refusal. Registration/green connection alone does not prove physical-image disclosure or revoke acceptance.

Capture does not resume sharing after restart, target replacement or OS lock. Registration does not change that consent policy. No recorder, global input trace or monitor/rectangle target is added.
