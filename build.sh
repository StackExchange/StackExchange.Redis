#!/usr/bin/env bash
set -e
source .env
dotnet restore
dotnet build --framework=netstandard2.0 StackExchange.Redis/StackExchange.Redis.csproj
