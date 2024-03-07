#!/bin/bash
set -eu
# Adapted from https://github.com/stewartadam/dotnet-x509-certificate-verification/blob/main/create_certificates.sh

base_dir="certificates"

create_ca() {
  local CA_CN="$1"
  local certificate_output="${base_dir}/${CA_CN}.pem"

  openssl genrsa -out "${base_dir}/${CA_CN}.key.pem" 2048 # Generate private key
  openssl req -x509 -new -nodes -key "${base_dir}/${CA_CN}.key.pem" -sha256 -days 9000 -out "${certificate_output}" -subj "/CN=${CA_CN}/O=MyDevices/C=US" # Generate root certificate

  echo -e "\nCertificate for CA ${CA_CN} saved to ${certificate_output}\n\n"
}

create_leaf_cert_req() {
  local DEVICE_CN="$1"

  openssl genrsa -out "${base_dir}/${DEVICE_CN}.key.pem" 2048 # new private key
  openssl req -new -key "${base_dir}/${DEVICE_CN}.key.pem" -out "${base_dir}/${DEVICE_CN}.csr.pem" -subj "/CN=${DEVICE_CN}/O=MyDevices/C=US" # generate signing request for the CA
}

sign_leaf_cert() {
  local DEVICE_CN="$1"
  local CA_CN="$2"
  local certificate_output="${base_dir}/${DEVICE_CN}.pem"

  openssl x509 -req -in "${base_dir}/${DEVICE_CN}.csr.pem" -CA ""${base_dir}/${CA_CN}.pem"" -CAkey "${base_dir}/${CA_CN}.key.pem" -set_serial 01 -out "${certificate_output}" -days 8999 -sha256 # sign the CSR

  echo -e "\nCertificate for ${DEVICE_CN} saved to ${certificate_output}\n\n"
}

mkdir -p "${base_dir}"

# Create one self-issued CA + signed cert
create_ca "ca.foo.com"
create_leaf_cert_req "device01.foo.com"
sign_leaf_cert "device01.foo.com" "ca.foo.com"