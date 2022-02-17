# instructions from https://www.envoyproxy.io/docs/envoy/latest/start/install
apt update
apt -y install debian-keyring debian-archive-keyring apt-transport-https curl lsb-release
curl -sL 'https://deb.dl.getenvoy.io/public/gpg.8115BA8E629CC074.key' | gpg --dearmor -o /usr/share/keyrings/getenvoy-keyring.gpg
echo "deb [arch=amd64 signed-by=/usr/share/keyrings/getenvoy-keyring.gpg] https://deb.dl.getenvoy.io/public/deb/debian $(lsb_release -cs) main" |  tee /etc/apt/sources.list.d/getenvoy.list
apt update
apt install getenvoy-envoy


