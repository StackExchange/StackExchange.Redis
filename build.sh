#!/usr/bin/env bash
set -e
dotnet restore
dotnet build --framework=netstandard2.0 StackExchange.Redis/StackExchange.Redis.csproj
