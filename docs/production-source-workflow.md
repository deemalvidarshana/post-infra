# Production source workflow

The `main` branch is the canonical source for production. The repository now
includes the features that previously existed only in the server working tree.

## Normal change flow

1. Pull the latest `main` branch before editing.
2. Make and test changes in a local checkout.
3. Commit and push the complete change to `main`.
4. Deploy with the Jenkins `sm` pipeline.
5. Confirm the Jenkins build, application HTTPS endpoint, and affected feature.

## Rules

- Do not edit files inside a running container. Container changes disappear on
  recreation and are not source control.
- Do not leave production-only changes uncommitted on the server.
- Never commit `.env` files, PEM keys, downloads, logs, build output, or runtime
  data. These paths are covered by `.gitignore`.
- If an emergency server-side edit is unavoidable, copy it back to a local
  checkout, test it, and commit it before the next Jenkins deployment.
- Tag the currently running Docker images before a risky deployment so an image
  rollback remains available.

## Production locations

- Server source: `/opt/sm-automate`
- Persistent downloads: `/opt/sm-automate/downloads`
- Jenkins pipeline: `sm`
- Compose project: `sm-automate`

The Jenkins pipeline uses `DOWNLOADS_HOST_PATH=/opt/sm-automate/downloads`, so
application deployments do not replace or relocate persistent download data.
