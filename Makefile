# tadmor-dotnet developer tasks.
#
# Restore reads only the committed feed in vendor/nuget (nuget.config):
# nothing is fetched from nuget.org, and no target touches the network
# except vendor-sync.
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE := 1
export DOTNET_GENERATE_ASPNET_CERTIFICATE := false
export TESTINGPLATFORM_TELEMETRY_OPTOUT := 1
DOTNET ?= dotnet
SERVER := src/Tadmor/bin/Release/net10.0/Tadmor

# Connection strings. Override on the command line, e.g.
#   make run DATABASE_URL=postgres://user:pass@host:5432/db
DATABASE_URL ?= postgres://tadmor:tadmor@127.0.0.1:5432/tadmor_dotnet?sslmode=disable
TEST_DATABASE_URL ?= postgres://tadmor:tadmor@127.0.0.1:5432/tadmor_dotnet_test?sslmode=disable
CONFORMANCE_DATABASE_URL ?= postgres://tadmor:tadmor@127.0.0.1:5432/tadmor_dotnet_conformance?sslmode=disable
HTTP_ADDR ?= 127.0.0.1:8080

.DEFAULT_GOAL := help
.PHONY: help build run adduser test conformance release clean vendor-check vendor-sync db

help: ## List available targets
	@grep -E '^[a-zA-Z_-]+:.*## ' $(MAKEFILE_LIST) | \
		awk 'BEGIN{FS=":.*## "}{printf "  make %-13s %s\n", $$1, $$2}'

build: ## Build the server and tests (Release)
	$(DOTNET) build Tadmor.slnx -c Release

run: build ## Build and run the server (migrates on start)
	DATABASE_URL='$(DATABASE_URL)' HTTP_ADDR=$(HTTP_ADDR) $(SERVER)

adduser: build ## Create or reset an administrator: make adduser EMAIL=... NAME=... (password on stdin)
	DATABASE_URL='$(DATABASE_URL)' $(SERVER) adduser --email="$(EMAIL)" --name="$(NAME)"

test: ## Run the test suite (integration tests wipe TEST_DATABASE_URL)
	TEST_DATABASE_URL='$(TEST_DATABASE_URL)' $(DOTNET) test --solution Tadmor.slnx -c Release

conformance: build ## Run tadmor's conformance suite against a fresh server (wipes the _conformance DB)
	DATABASE_URL='$(CONFORMANCE_DATABASE_URL)' SERVER=$(SERVER) tools/conformance.sh $(ARGS)

release: ## Publish the self-contained linux-x64 server into bin/release
	rm -rf bin/release
	$(DOTNET) publish src/Tadmor -c Release -r linux-x64 --self-contained -o bin/release

clean: ## Remove build output
	rm -rf bin src/*/bin src/*/obj tests/*/bin tests/*/obj

vendor-check: ## Verify vendor/nuget matches vendor/lock.txt (offline)
	tools/vendor.py check

vendor-sync: ## Re-resolve vendor/nuget from nuget.org and rewrite the locks (network)
	tools/vendor.py sync

db: ## Start a local Postgres 17 container (podman) with the dev, test, and conformance databases
	podman run -d --name tadmor-dotnet-pg -e POSTGRES_USER=tadmor -e POSTGRES_PASSWORD=tadmor \
		-e POSTGRES_DB=tadmor_dotnet -p 127.0.0.1:5432:5432 docker.io/library/postgres:17
	until podman exec tadmor-dotnet-pg pg_isready -U tadmor -q; do sleep 1; done
	podman exec tadmor-dotnet-pg createdb -U tadmor tadmor_dotnet_test
	podman exec tadmor-dotnet-pg createdb -U tadmor tadmor_dotnet_conformance
