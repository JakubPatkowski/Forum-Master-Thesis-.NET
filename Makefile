# forum-dotnet — developer front door.  Run `make help`.
# Targets delegate to scripts/*.sh (bash; Linux/WSL). Invoked via `bash` so the
# scripts work even without the executable bit set.
SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

.PHONY: help preflight infra-up infra-down api web migrate seed test format build \
        images scan mk-up mk-deploy mk-down mk-reset-db mk-tls tunnels load pods logs urls \
        mon-up mon-down mon-check

help: ## Show this help
	@awk 'BEGIN{FS=":.*##"; printf "\nforum-dotnet make targets:\n\n"} \
	     /^[a-zA-Z0-9_-]+:.*##/ {printf "  \033[36m%-12s\033[0m %s\n",$$1,$$2} \
	     END{print ""}' $(MAKEFILE_LIST)

## --- Local development (docker compose + dotnet) ---------------------------
preflight:  ## Check the toolchain (docker/kubectl/minikube/dotnet/k6)
	@bash scripts/preflight.sh
infra-up:   ## Start Postgres + RabbitMQ + MinIO
	@bash scripts/infra-up.sh
infra-down: ## Stop local infra   (make infra-down ARGS=--volumes to wipe data)
	@bash scripts/infra-down.sh $(ARGS)
api:        ## Run the API locally (make api ARGS=--migrate to migrate first)
	@bash scripts/dev-api.sh $(ARGS)
web:        ## Run the frontend dev server (npm install if needed) on :3000
	@bash scripts/dev-web.sh
migrate:    ## Apply migrations + views against local infra
	@cd backend && dotnet run --project src/Bootstrap/Forum.Api -- migrate
seed:       ## Seed deterministic data (make seed [ARGS="--benchmark"] [ARGS="--cluster"]) — Development → forum_net, Benchmark → forum_net_bench
	@bash scripts/seed-test-data.sh $(ARGS)
build:      ## dotnet build the solution
	@cd backend && dotnet build Forum.slnx
test:       ## dotnet test (needs Docker for Testcontainers)
	@cd backend && dotnet test Forum.slnx
format:     ## dotnet format
	@cd backend && dotnet format Forum.slnx

## --- Docker images -----------------------------------------------------------
images:     ## Build API + web images, tag git-<sha>[-dirty] (make images ARGS=--no-cache)
	@bash scripts/build-images.sh $(ARGS)
scan:       ## Trivy-scan both images, HIGH/CRITICAL fixed-only (needs trivy)
	@bash scripts/scan-image.sh $(ARGS)

## --- Cluster (minikube) ----------------------------------------------------
mk-up:       ## Start the minikube cluster (calico CNI, ingress, metrics-server)
	@bash scripts/setup-minikube.sh
mk-deploy:   ## Build images + deploy everything (make mk-deploy ARGS=--seed|--seed-benchmark)
	@bash scripts/deploy.sh $(ARGS)
mk-down:     ## Tear down (make mk-down ARGS=--stop|--delete)
	@bash scripts/teardown.sh $(ARGS)
mk-reset-db: ## Wipe the in-cluster DB and re-migrate
	@bash scripts/reset-db.sh
mk-tls:      ## One-time: mint the mkcert TLS cert + forum-tls secret
	@bash scripts/mkcert-tls.sh $(ARGS)
tunnels:     ## Port-forward all admin/dev services to localhost (Windows-reachable); Ctrl+C stops
	@bash scripts/dev-tunnels.sh

## --- Monitoring (Phase 10c: Helm — kube-prometheus-stack/Loki/Alloy/Tempo/pg-exporter) ------
mon-up:      ## Install/upgrade the monitoring stack (pinned Helm charts + dashboards + rules)
	@bash scripts/monitoring-up.sh
mon-down:    ## Uninstall the monitoring stack + delete the monitoring namespace (reclaims RAM)
	@bash scripts/monitoring-down.sh
mon-check:   ## Assert all Prometheus targets UP + Loki ingesting + Tempo ready
	@bash scripts/monitoring-check.sh

## --- Ops -------------------------------------------------------------------
load: ## Run a k6 load profile (make load ARGS=smoke|demo|stress)
	@bash scripts/run-load-test.sh $(ARGS)
pods: ## Show cluster resources
	@kubectl -n forum-dotnet get pods,svc,ingress,hpa
logs: ## Tail backend logs
	@kubectl -n forum-dotnet logs -l app=backend -f
urls: ## Print access URLs
	@echo "From WSL:      https://forum.local  (minikube ip: $$(minikube -p forum ip 2>/dev/null || echo '<cluster down>') in /etc/hosts)"
	@echo "Grafana:       https://grafana.forum.local  (admin/admin; needs make mon-up)"
	@echo "From Windows:  make tunnels  +  hosts-file 127.0.0.1 entries  (docs/runbooks/wsl-minikube-setup.md)"
