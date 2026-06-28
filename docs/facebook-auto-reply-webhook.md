# Facebook comment auto-reply webhook

The app exposes Meta callbacks through the existing HTTPS frontend proxy.
Use one callback per saved Meta Developer App:

```text
https://smautomate.duckdns.org/api/smapi/FacebookWebhooks/meta/{webhookKey}
```

The exact callback URL and verify token are shown in the app at:

```text
Facebook Pages -> Meta Apps
```

The legacy single-app env fallback still exists:

- `FACEBOOK_WEBHOOK_VERIFY_TOKEN`
- `FACEBOOK_APP_SECRET`

Prefer the frontend Meta Apps screen for multi-owner/page setups.

## Meta app setup

1. In Meta for Developers, open the app and add Webhooks.
2. Select the Page object.
3. Set the callback URL shown for that saved Meta App.
4. Set the verify token shown for that saved Meta App.
5. Subscribe to the Page `feed` field.
6. Make sure the Page access token has the needed Page permissions, including
   `pages_manage_metadata`, `pages_read_engagement`, and
   `pages_manage_engagement`.
7. In SM Automate, link each Facebook Page to the correct saved Meta App.
8. Use the page card's "Subscribe Webhook" button, or subscribe the connected
   Page to the app using the Page `subscribed_apps` Graph API edge with
   `subscribed_fields=feed`.

For example:

```text
My Meta App:
https://smautomate.duckdns.org/api/smapi/FacebookWebhooks/meta/my-app-a1b2c3

Brother's Meta App:
https://smautomate.duckdns.org/api/smapi/FacebookWebhooks/meta/ayya-meta-app-d4e5f6
```

The same server receives both, then routes each event through the `webhookKey`
to the correct app/user/page settings.

## Runtime flow

1. Meta sends comment events to the webhook.
2. `FacebookWebhooksController` verifies the callback and receives POST events.
3. `FacebookWebhookReceiver` stores comment events and skips duplicates.
4. `FacebookCommentReplyWorker` generates AI replies from the saved Gemini
   settings.
5. Manual approval mode stores the draft until a user approves it in the UI.
6. Auto mode publishes the reply through the Graph API comment replies edge.
