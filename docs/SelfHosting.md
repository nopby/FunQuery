# Self-hosting the Documentation

The documentation is a static MkDocs Material site. It can be served from any machine on your
Tailscale tailnet without GitHub Pages and without exposing it to the public internet.

Tailscale Serve prints the URL for the machine, typically:

```text
https://<server>.<tailnet>.ts.net/
```

The rest of this guide uses that placeholder.

## How it works

```text
private FunQuery repo
        │
        ▼
mkdocs build --strict
        │
        ▼
site/ (static HTML/CSS/JS)
        │
        ▼
Tailscale Serve
        │
        ▼
https://<server>.<tailnet>.ts.net/
```

Tailscale Serve is tailnet-only. Do not use `tailscale funnel` for private documentation.

## Prerequisites on the server

* Python 3.10 or newer with `venv`.
* Git.
* Tailscale, connected to the same tailnet.
* MagicDNS and HTTPS certificates enabled for the tailnet.

On Debian/Ubuntu:

```bash
sudo apt update
sudo apt install -y git python3 python3-venv
```

## Build the site

Clone the private repository on the server, or update an existing checkout:

```bash
git clone https://github.com/nopby/FunQuery.git
cd FunQuery
git pull --ff-only
```

Create a virtual environment and build:

```bash
python3 -m venv .venv-docs
.venv-docs/bin/pip install -r requirements-docs.txt
.venv-docs/bin/mkdocs build --strict
```

The output is in `site/`.

The helper script does the same and can also start Tailscale Serve:

```bash
./scripts/serve-docs.sh --build-only
./scripts/serve-docs.sh
```

## Serve with Tailscale

```bash
sudo tailscale serve --bg "$PWD/site"
sudo tailscale serve status
```

If the Tailscale binary is not on `sudo`'s PATH:

```bash
sudo /usr/bin/tailscale serve --bg "$PWD/site"
```

The URL is shown by `tailscale serve status`, for example:

```text
https://<server>.<tailnet>.ts.net/
```

Open it from any device that is connected to the same tailnet and allowed by the Tailscale ACL.

### If serving a directory is not available

Run a local static server bound to loopback, then proxy it through Tailscale:

```bash
python3 -m http.server 8000 --bind 127.0.0.1 --directory site
sudo tailscale serve --bg 8000
```

Keep the local server running as a systemd service or a terminal session.

## Update the site

After pulling changes:

```bash
cd /path/to/FunQuery
git pull --ff-only
.venv-docs/bin/mkdocs build --strict
```

Tailscale Serve keeps serving `site/`; no restart is needed when the files change.

Optional cron entry (every 10 minutes):

```cron
*/10 * * * * cd /opt/FunQuery && git pull --ff-only && .venv-docs/bin/mkdocs build --strict
```

For a production-like setup, use a systemd timer instead of cron.

## Stop or reset Serve

```bash
sudo tailscale serve off
```

To clear the saved Serve configuration:

```bash
sudo tailscale serve reset
```

## Security

* Use `tailscale serve`, never `tailscale funnel`, for private docs.
* Restrict access with Tailscale ACLs (users, devices, or tags).
* If you run a local web server, bind it to `127.0.0.1`, not `0.0.0.0`.
* The private repository still needs a read-only deploy key or equivalent credentials on the server.
* Do not put secrets, connection strings, or internal credentials in the documentation.

## Troubleshooting

| Symptom | Fix |
| ------- | --- |
| `tailscale: command not found` | Use `/usr/bin/tailscale` or add Tailscale to `PATH` |
| Browser shows 404 | Check the Serve target points at the directory containing `index.html` (`site/`) |
| HTTPS certificate warning | Enable HTTPS certificates and MagicDNS in the Tailscale admin console |
| Page is reachable from outside the tailnet | You used `tailscale funnel`; switch to `tailscale serve` |
| Changes do not appear | Re-run `mkdocs build --strict` and confirm `site/` is the served directory |
| Permission denied reading `site/` | Run Serve with a user that can read the repository, or use `sudo` |
