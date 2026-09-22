SHELL := /bin/sh
.DEFAULT_GOAL := help

# Rewriting and test reports share the same output directory.
.NOTPARALLEL:

CONFIGURATION ?= Debug
ITERATIONS ?= 100
METHOD ?= DetectLostUpdate
export DOTNET_ROLL_FORWARD ?= Major
PROJECT := CoyoteTests/CoyoteTests.csproj
TOOL_MANIFEST := .config/dotnet-tools.json
COYOTE_TEST_DLL := CoyoteTests/bin/$(CONFIGURATION)/net9.0/CoyoteTests.dll

.PHONY: help setup build test replay clean

help:
	@echo "make setup          - 依存ライブラリとCoyoteツールをセットアップ（初回）"
	@echo "make build          - ビルド"
	@echo "make test           - Coyoteで探索テストを実行"
	@echo "make replay TRACE=<パス> [METHOD=<テスト名>] - 失敗トレースを再現"
	@echo "make clean          - ビルド成果物をクリーン"
	@echo "変数: CONFIGURATION=Debug ITERATIONS=100"

setup:
	dotnet restore "$(PROJECT)" --disable-parallel
	dotnet tool restore --tool-manifest "$(TOOL_MANIFEST)"

build:
	dotnet build "$(PROJECT)" --configuration "$(CONFIGURATION)" --no-restore

test: build
	dotnet tool run coyote rewrite "$(COYOTE_TEST_DLL)"
	dotnet tool run coyote test "$(COYOTE_TEST_DLL)" -i "$(ITERATIONS)"

replay:
	@test -n "$(TRACE)" || { echo "TRACE=<トレースのパス> を指定してください" >&2; exit 1; }
	@test -f "$(TRACE)" || { echo "トレースが見つかりません: $(TRACE)" >&2; exit 1; }
	dotnet tool run coyote replay "$(COYOTE_TEST_DLL)" "$(TRACE)" -m "$(METHOD)"

clean:
	dotnet clean "$(PROJECT)" --configuration "$(CONFIGURATION)"
