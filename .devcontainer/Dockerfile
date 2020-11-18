ARG INSTALL_NODE=false
ARG INSTALL_AZURE_CLI=false
FROM mcr.microsoft.com/vscode/devcontainers/dotnet:dev-5.0

ENV DOTNET_NOLOGO=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=true
ENV DEVCONTAINER=true

# install redis-cli and ping
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends iputils-ping redis-tools 
