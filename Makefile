# forum-dotnet — developer front door.  Run `make help`.
# Targets delegate to scripts/*.sh (bash; Linux/WSL). Invoked via `bash` so the
# scripts work even without the executable bit set.
SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

.PHONY: help preflight infra-up infra-down api migrate test format build \
        mk-up mk-deploy mk-down mk-reset-db load pods logs urls

help: ## Show this help
	@awk 'BEGIN{FS":.*##"; printf "\nforum-dotnet make targets:\n\n"} \
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
migrate:    ## Apply migrations + views against local infra
	@cd backend && dotnet run --project src/Bootstrap/Forum.Api -- migrate
build:      ## dotnet build the solution
	@cd backend && dotnet build Forum.slnx
test:       ## dotnet test (needs Docker for Testcontainers)
	@cd backend && dotnet test Forum.slnx
format:     ## dotnet format
	@cd backend && dotnet format Forum.slnx

## --- Cluster (minikube) ----------------------------------------------------
mk-up:       ## Start the minikube cluster
	@bash scripts/setup-minikube.sh
mk-deploy:   ## Build image + apply all manifests
	@bash scripts/deploy.sh
mk-down:     ## Tear down (make mk-down ARGS=--stop|--delete)
	@bash scripts/teardown.sh $(ARGS)
mk-reset-db: ## Wipe the in-cluster DB and re-migrate
	@bash scripts/reset-db.sh

## --- Ops -------------------------------------------------------------------
load: ## Run a k6 load profile (make load ARGS=smoke|demo|stress)
	@bash scripts/run-load-test.sh $(ARGS)
pods: ## Show cluster resources
	@kubectl -n forum-dotnet get pods,svc,ingress,hpa
logs: ## Tail backend logs
	@kubectl -n forum-dotnet logs -l app=backend -f
urls: ## Print access URLs
	@echo "Ingress:  http://forum.local/api   (minikube ip: $$(minikube -p forum ip 2>/dev/null || echo '<cluster down>'))"
