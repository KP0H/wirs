# Grafana Dashboard

The default Grafana dashboard for the Webhook Inbox stack is provisioned automatically from `docker/grafana/dashboards/webhookinbox.json` when you run `docker compose up`.

## Manual Import

1. Sign in to Grafana (default credentials `admin` / `admin`).
2. Navigate to **Dashboards → New → Import**.
3. Either upload `docker/grafana/dashboards/webhookinbox.json` or paste its contents into the import form.
4. Select the `Prometheus` datasource (provisioned with UID `prometheus`).
5. Click **Import** to create the dashboard.

The dashboard highlights:
- API ingest rate split by source.
- Worker delivery attempts by result.
- Signature validation failures, idempotent hits, and rate-limit blocks over the last 15 minutes.

## Datasource

The Prometheus datasource is defined in `docker/grafana/provisioning/datasources/datasource.yml` and points to the Prometheus container (`http://prometheus:9090`). If you are importing the dashboard into another Grafana instance, ensure a Prometheus datasource with UID `prometheus` is available.
