# Kubernetes Deployment

SenseİK uygulaması için Kubernetes deployment manifests.

## Yapı

```
k8s/
├── base/                  # Paylaşılan base manifestler
│   ├── namespace.yaml
│   ├── postgres.yaml
│   ├── redis.yaml
│   ├── minio.yaml
│   ├── api.yaml
│   ├── worker.yaml
│   ├── web.yaml
│   ├── mailpit.yaml
│   └── kustomization.yaml
└── overlays/
    ├── dev/               # Development overlay
    │   └── kustomization.yaml
    └── prod/              # Production overlay
        ├── kustomization.yaml
        ├── ingress.yaml
        ├── hpa.yaml
        └── network-policies.yaml
```

## Kullanım

### Development Deployment

```bash
# Build images
docker build -f src/Sense.Api/Dockerfile -t senseik-api:latest .
docker build -f src/Sense.Worker/Dockerfile -t senseik-worker:latest .
docker build -f web/Dockerfile -t senseik-web:latest .

# Load images (kind/minikube için)
kind load docker-image senseik-api:latest
kind load docker-image senseik-worker:latest
kind load docker-image senseik-web:latest

# Deploy
kubectl apply -k k8s/overlays/dev

# Status check
kubectl get pods -n senseik-dev
kubectl get svc -n senseik-dev

# Port forward
kubectl port-forward -n senseik-dev svc/dev-api 8080:8080
kubectl port-forward -n senseik-dev svc/dev-web 3000:8080
kubectl port-forward -n senseik-dev svc/dev-postgres 5432:5432
```

### Production Deployment

```bash
# Build ve push images
docker build -f src/Sense.Api/Dockerfile -t registry.example.com/senseik-api:v1.0.0 .
docker build -f src/Sense.Worker/Dockerfile -t registry.example.com/senseik-worker:v1.0.0 .
docker build -f web/Dockerfile -t registry.example.com/senseik-web:v1.0.0 .

docker push registry.example.com/senseik-api:v1.0.0
docker push registry.example.com/senseik-worker:v1.0.0
docker push registry.example.com/senseik-web:v1.0.0

# overlays/prod/kustomization.yaml içinde image tag'ları update et
# newTag: v1.0.0 olarak değiştir

# IMPORTANT: Secrets'ı configure et (before deploying)
# overlays/prod/kustomization.yaml dosyasında şunları update et:
# - postgres-secret: password
# - minio-secret: rootPassword  
# - api-secret: databaseConnection, sentryDsn

# Deploy
kubectl apply -k k8s/overlays/prod

# Status
kubectl get pods -n senseik-prod
kubectl get svc -n senseik-prod
kubectl get ingress -n senseik-prod
```

## Production Features

### Prod Overlay İçeriği

1. **High Availability**
   - API: 3 replicas (Pod Anti-Affinity)
   - Worker: 2 replicas
   - Web: 2 replicas

2. **Auto Scaling (HPA)**
   - API: min 2, max 5 replicas (CPU 70%, Memory 80%)
   - Worker: min 2, max 5 replicas (CPU 75%, Memory 85%)
   - Web: min 2, max 4 replicas (CPU 80%)

3. **Network Security**
   - Network Policies (deny-all by default)
   - Service-to-service communication sadece gerekli portlar
   - External access sadece Ingress üzerinden

4. **Ingress & TLS**
   - HTTPS with Let's Encrypt
   - api.example.com → API
   - app.example.com → Web

5. **Resource Management**
   - CPU/Memory requests ve limits
   - Stricter than dev

## Database Migrations

Migrations otomatik çalışır API pod'u başlanırken (init container).

Manual migration:
```bash
kubectl exec -it -n senseik-dev deploy/dev-api -- \
  dotnet Sense.Migrator.dll migrate
```

## Environment Variables

### API
- `ASPNETCORE_ENVIRONMENT`: Production/Development
- `ConnectionStrings__Database`: PostgreSQL
- `ConnectionStrings__Redis`: Redis
- `Storage__Provider`: MinIO
- `Email__Smtp__Host`: Mail server
- `Frontend__BaseUrl`: Web URL
- `Seeding__Enabled`: Demo data (dev only)

### Worker
- `DOTNET_ENVIRONMENT`: Production/Development  
- Same connection strings as API

### Web
- Nginx proxies /api → API service
- VITE_API_BASE_URL: (build-time)

## Troubleshooting

```bash
# Logs
kubectl logs -n senseik-dev deploy/dev-api -f

# Pod events
kubectl describe pod -n senseik-dev <pod-name>

# Get all events
kubectl get events -n senseik-dev

# Database access
kubectl exec -it -n senseik-dev svc/dev-postgres -- \
  psql -U sense -d senseik

# Redis
kubectl exec -it -n senseik-dev svc/dev-redis -- redis-cli

# MinIO console
kubectl port-forward -n senseik-dev svc/dev-minio 9001:9001
# minio / minio12345
```

## Secrets Management

Üretimde güvenli secret handling için:
- Sealed Secrets
- External Secrets Operator
- HashiCorp Vault
- AWS Secrets Manager / Azure Key Vault

Şimdilik basit secretGenerator kullanıyoruz.

## CI/CD

```bash
# GitOps için
kubectl apply -k k8s/overlays/prod --dry-run=client -o yaml > manifests.yaml

# Validate
kustomize build k8s/overlays/prod

# Rollout
kubectl rollout status deployment/dev-api -n senseik-dev

# Rollback
kubectl rollout undo deployment/dev-api -n senseik-dev
```

## Notes

- PostgreSQL ve Redis: StatefulSet (persistent)
- API, Worker: Deployment (stateless)
- Web: Nginx on Deployment (stateless)
- Services: ClusterIP (Ingress üzerinden exposed)
- Init containers: database migration + wait-for-dependencies
- Health checks: liveness + readiness probes
