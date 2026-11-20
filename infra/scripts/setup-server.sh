#!/usr/bin/env bash
# One-time setup for a fresh Ubuntu/Debian VPS: a sudo user, Docker + Compose, UFW.
# Run as root (or with sudo) once, over a console/rescue session — not over the deploy user
# you are about to create. Re-running is safe; every step is idempotent.
#
# Usage: ./setup-server.sh <deploy-username> <path-to-public-ssh-key>

set -euo pipefail

USERNAME="${1:?Usage: setup-server.sh <deploy-username> <path-to-public-ssh-key>}"
PUBKEY_FILE="${2:?Usage: setup-server.sh <deploy-username> <path-to-public-ssh-key>}"

if [ "$(id -u)" -ne 0 ]; then
  echo "Run this as root (sudo ./setup-server.sh ...)." >&2
  exit 1
fi

echo "== Updating the system =="
apt-get update -y
apt-get upgrade -y

echo "== Creating sudo user '$USERNAME' (key-only login) =="
if ! id -u "$USERNAME" >/dev/null 2>&1; then
  adduser --disabled-password --gecos "" "$USERNAME"
  usermod -aG sudo "$USERNAME"
fi

install -d -m 700 -o "$USERNAME" -g "$USERNAME" "/home/$USERNAME/.ssh"
install -m 600 -o "$USERNAME" -g "$USERNAME" "$PUBKEY_FILE" "/home/$USERNAME/.ssh/authorized_keys"

echo "== Disabling root login and password authentication over SSH =="
SSHD_DROPIN=/etc/ssh/sshd_config.d/99-bandup-hardening.conf
cat > "$SSHD_DROPIN" <<'EOF'
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
EOF
systemctl reload ssh

echo "== Installing Docker Engine and the Compose plugin =="
if ! command -v docker >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  ARCH="$(dpkg --print-architecture)"
  CODENAME="$(. /etc/os-release && echo "$VERSION_CODENAME")"
  echo "deb [arch=$ARCH signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $CODENAME stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -y
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
fi
usermod -aG docker "$USERNAME"

echo "== Installing git =="
apt-get install -y git

echo "== Setting the timezone to UTC =="
timedatectl set-timezone UTC

echo "== Configuring the firewall (SSH, HTTP, HTTPS only) =="
apt-get install -y ufw
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

echo "== Done =="
echo "Log in as '$USERNAME' from now on. Re-run 'newgrp docker' or log out/in for the docker group to take effect."
