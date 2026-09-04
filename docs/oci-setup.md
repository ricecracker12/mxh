# Hướng dẫn setup server OCI (staging) — SocialMedia

> Môi trường staging thật trên Internet, dựng **sớm** (ngay sau GĐ0 của kế hoạch) để mỗi giai đoạn
> sau đều deploy liên tục và test bằng tài khoản thật. Hướng deploy: **docker-compose + Caddy** (tay).
>
> ⚠️ **Chọn 1 hướng, không trộn:** hoặc theo tài liệu này (compose + Caddy — hướng chính thức của kế
> hoạch), hoặc **Dokploy** (một PaaS tự host thay thế). Nếu chốt Dokploy thì các mục iii/vii/viii dưới
> đây được thay bằng "cài Dokploy + nối Git".
>
> 🔴 **OCI Ampere A1 là ARM64** — mọi image phải `linux/arm64` (multi-arch). .NET 8 / Postgres 16 /
> Redis 7 / Caddy đều có arm64 sẵn; riêng image API **tự build phải build cho arm64** (xem mục vii).

---

## i–ii. Tiền đề (đã làm trước đó)
- VPS OCI Ampere A1 (2 OCPU / 12GB), Ubuntu LTS, public IP, user non-root.
- SSH **key-only** (tắt password), `fail2ban`.
- Firewall máy (`ufw`) chỉ mở **80/443** (+ 22 giới hạn IP).

---

## iii. Cài Docker + Compose + thư mục deploy

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" \
| sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

sudo usermod -aG docker $USER   # rồi ĐĂNG XUẤT / ĐĂNG NHẬP lại
mkdir -p ~/app/deploy
```

**Kiểm tra:** `docker compose version` → v2.x · `docker run --rm hello-world` chạy · `uname -m` = `aarch64`.

---

## v. Cloudflare R2 (chỉ 1 bucket staging — local dùng MinIO)

> Quyết định dự án: **local dev dùng MinIO** (S3-compatible, trong docker-compose dev), KHÔNG đụng R2.
> Vì vậy R2 chỉ cần **1 bucket cho staging**. Khi lên production thật mới tạo thêm `socialmedia-prod`.
> → **Không tạo `socialmedia-dev`.**

Trên dashboard Cloudflare → **R2**:

1. **Create bucket:** `socialmedia-staging`.
2. **API token:** *Manage R2 API Tokens* → *Create API Token* → quyền **Object Read & Write**, giới hạn
   đúng bucket này. Lưu (chỉ hiện 1 lần): `Access Key ID`, `Secret Access Key`, endpoint
   `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`.
3. **CORS policy** (bucket → *Settings* → *CORS Policy*) — cho trình duyệt PUT thẳng bằng pre-signed URL:
```json
[
  {
    "AllowedOrigins": ["https://staging.tenmien.com", "http://localhost:3000"],
    "AllowedMethods": ["PUT", "GET"],
    "AllowedHeaders": ["Content-Type"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3600
  }
]
```

Bucket để **private**; API dùng AWS S3 SDK trỏ endpoint R2 để **ký** pre-signed URL — ảnh không đi qua API.

**Kiểm tra:** `aws s3 ls --endpoint-url https://4b3df3677ff98e45e42beaab5c3aea6f.r2.cloudflarestorage.com` thấy bucket;
test 1 pre-signed PUT từ trình duyệt (khi có API) → 200, không lỗi CORS.

> Muốn local cũng bắn thẳng R2 thay vì MinIO? Dùng chung bucket này nhưng tách bằng key prefix:
> `R2__Prefix=dev` (local) vs `R2__Prefix=staging` (server) — dọn dev chỉ việc xoá theo prefix,
> không đụng data staging dùng demo.

---

## vi. Secrets (không commit / không nhúng image)

**A. Trên server** — `~/app/deploy/.env` (compose đọc qua `env_file`):
```bash
cd ~/app/deploy
umask 077
cat > .env <<'EOF'
POSTGRES_PASSWORD=<openssl rand -base64 24>
ConnectionStrings__Postgres=Host=postgres;Database=socialapp;Username=socialapp;Password=<trùng trên>
Jwt__SigningKey=<openssl rand -base64 48>
ConnectionStrings__Redis=redis:6379
R2__Endpoint=https://<gido>.r2.cloudflarestorage.com
R2__Bucket=socialmedia-staging
R2__AccessKey=<access key id>
R2__SecretKey=<secret access key>

# Email (gửi mail xác minh đăng ký + reset mật khẩu — ACT-ES / FR-001).
# Staging/Prod: SMTP thật, ví dụ Brevo free (~300 mail/ngày). Local dev thì dùng
# Mailpit trong compose (Host=mailpit, Port=1025, không cần User/Password).
Smtp__Host=smtp-relay.brevo.com
Smtp__Port=587
Smtp__User=<smtp username / api key>
Smtp__Password=<smtp password>
Smtp__From=no-reply@tenmien.com
EOF
chmod 600 .env
```
> ASP.NET Core map biến `Section__Key` → config (`__` = lồng cấp). Sinh khóa: `openssl rand -base64 48`.
>
> **Email theo môi trường:** *local dev* = **Mailpit** (compose dev, `Smtp__Host=mailpit` `Smtp__Port=1025`,
> mail giả xem ở web UI `:8025`, không cần tài khoản); *staging/prod* = SMTP thật (Brevo / Resend /
> Mailtrap / Gmail App Password) — đây là giả định **ISS-03** trong PTTK. Chưa làm chức năng email thì
> để trống cũng được, server vẫn chạy; đến **GĐ1 (Identity — đăng ký + xác minh email)** mới thực sự cần.

**B. Trên CI** (GitHub → Settings → Secrets → Actions): `STAGING_HOST`, `STAGING_USER`, `STAGING_SSH_KEY`.

**Kiểm tra:** `git ls-files | grep -i env` rỗng · `.env` quyền `-rw-------` · `.gitignore` có `.env`.

---

## vii. CD pipeline: build (arm64) → push → SSH deploy

> **Repo = `mxh`.** File này đặt tại `mxh/.github/workflows/deploy-staging.yml` (đúng chuẩn — workflow
> phải nằm ở gốc repo). `${{ github.repository }}` tự thành `<username>/mxh` → image tự là
> `ghcr.io/<username>/mxh/api`. `context: .` = gốc repo `mxh` ⇒ Dockerfile ở `mxh/Dockerfile`,
> publish `src/SocialApp.Api/SocialApp.Api.csproj`.

`.github/workflows/deploy-staging.yml`:
```yaml
name: deploy-staging
on:
  push:
    branches: [develop]
jobs:
  build:
    runs-on: ubuntu-latest
    permissions: { contents: read, packages: write }
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-qemu-action@v3
      - uses: docker/setup-buildx-action@v3
      - uses: docker/login-action@v3
        with: { registry: ghcr.io, username: ${{ github.actor }}, password: ${{ secrets.GITHUB_TOKEN }} }
      - uses: docker/build-push-action@v6
        with:
          context: .
          platforms: linux/arm64
          push: true
          tags: |
            ghcr.io/${{ github.repository }}/api:staging
            ghcr.io/${{ github.repository }}/api:${{ github.sha }}
  deploy:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.STAGING_HOST }}
          username: ${{ secrets.STAGING_USER }}
          key: ${{ secrets.STAGING_SSH_KEY }}
          script: |
            cd ~/app/deploy
            echo ${{ secrets.GITHUB_TOKEN }} | docker login ghcr.io -u ${{ github.actor }} --password-stdin
            docker compose -f docker-compose.staging.yml pull
            docker compose -f docker-compose.staging.yml run --rm migrate
            docker compose -f docker-compose.staging.yml up -d
            docker image prune -f
```
> Migration chạy trong bước deploy (service `migrate`), **KHÔNG auto-migrate lúc app start**.

**Kiểm tra:** push commit lên `develop` → Actions xanh → staging tự cập nhật, không thao tác tay.

---

## viii. compose staging: Caddy → API → Postgres + Redis

`~/app/deploy/docker-compose.staging.yml`:
```yaml
name: socialmedia-staging
networks: { edge: , internal: }
volumes: { pgdata: , caddy_data: , caddy_config: }

services:
  caddy:                              # cổng vào duy nhất, chỉ nó mở 80/443
    image: caddy:2
    restart: unless-stopped
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - /etc/ssl/cloudflare:/etc/ssl/cloudflare:ro   # tái dùng cert wildcard *.banhgao.net có sẵn trên host
      - caddy_data:/data
      - caddy_config:/config
    networks: [edge, internal]
    depends_on: [api]

  api:
    image: ghcr.io/ricecracker12/30inf067_btl/api:staging   # repo name viết THƯỜNG toàn bộ (Docker không nhận chữ hoa)
    restart: unless-stopped
    env_file: [./.env]
    environment:
      ASPNETCORE_ENVIRONMENT: Staging
      ASPNETCORE_URLS: http://+:8080
    networks: [internal]              # KHÔNG mở port ra host
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health/ready"]
      interval: 15s
      timeout: 3s
      retries: 3
    depends_on:
      postgres: { condition: service_healthy }
      redis: { condition: service_healthy }

  migrate:                            # one-shot, CD gọi trước khi up api
    image: ghcr.io/ricecracker12/30inf067_btl/api:staging   # repo name viết THƯỜNG toàn bộ (Docker không nhận chữ hoa)
    profiles: ["tools"]
    env_file: [./.env]
    entrypoint: ["dotnet", "SocialApp.Api.dll", "--migrate"]
    networks: [internal]
    depends_on:
      postgres: { condition: service_healthy }

  postgres:
    image: postgres:16               # theo PTTK (Postgres 16)
    restart: unless-stopped
    environment:
      POSTGRES_DB: socialapp
      POSTGRES_USER: socialapp
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes: [pgdata:/var/lib/postgresql/data]
    networks: [internal]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U socialapp -d socialapp"]
      interval: 10s
      timeout: 3s
      retries: 5

  redis:
    image: redis:7
    restart: unless-stopped
    networks: [internal]
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 5
```
Postgres/redis **không có `ports:`** → không lộ ra ngoài. Chỉ `caddy` mở 80/443.

**TLS với Cloudflare** (tránh bẫy ACME khi bật proxy cam): dùng **Cloudflare Origin Certificate**.
Ở đây **tái dùng cert wildcard `*.banhgao.net` đã có sẵn** trên host tại
`/etc/ssl/cloudflare/banhgao.net.pem` + `.key` (được bind-mount vào container ở mục compose trên).
Đặt SSL mode Cloudflare = **Full (strict)**.
> ⚠️ Cert `*.banhgao.net` **chỉ khớp domain dưới `banhgao.net`** (vd `mxh.banhgao.net`), KHÔNG khớp
> `banhgao.com` hay apex `banhgao.net`. Domain phục vụ phải là subdomain 1 cấp của `banhgao.net`.

`~/app/deploy/Caddyfile`:
```
mxh.banhgao.net {
    encode gzip
    tls /etc/ssl/cloudflare/banhgao.net.pem /etc/ssl/cloudflare/banhgao.net.key
    reverse_proxy api:8080
}
```

Khởi động lần đầu:
```bash
cd ~/app/deploy
docker compose -f docker-compose.staging.yml run --rm migrate
docker compose -f docker-compose.staging.yml up -d
docker compose -f docker-compose.staging.yml ps
```

**Kiểm tra:** `curl -I https://mxh.banhgao.net/health/ready` → 200 · `docker compose ps` tất cả `healthy`
· `nmap -Pn <IP>` chỉ 80/443 · `psql -h <IP>` timeout (DB không public).

### Cần chuẩn bị/sửa thêm cho mục viii
- **File cùng thư mục `~/app/deploy/`:** `docker-compose.staging.yml` + `.env` (mục vi) + `Caddyfile`
  (domain `mxh.banhgao.net`). Cert dùng lại wildcard `*.banhgao.net` có sẵn ở `/etc/ssl/cloudflare/`
  (bind-mount vào caddy), **không cần** thư mục `origin-cert/`.
- **`${POSTGRES_PASSWORD}`** được Compose thay từ `.env` **cùng thư mục** → `.env` bắt buộc nằm cạnh compose.
- **`POSTGRES_USER`/`POSTGRES_DB`** phải khớp connection string trong `.env` (`Username=app;Database=socialmedia`).
- **Dockerfile** `mxh/Dockerfile` (context = gốc repo): multi-stage, `linux/arm64`, non-root, output `SocialApp.Api.dll`.
- **Migrate:** app đã nhận cờ `--migrate`. **GĐ0 là no-op thoát 0** (chưa có DbContext) nên service
  `migrate` chạy xong ngay; từ **GĐ1** cờ này sẽ apply EF migration thật rồi thoát.
- ⚠️ **Healthcheck cần `curl` nhưng image `aspnet` không có sẵn.** Chọn 1:
  - Thêm vào Dockerfile: `RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*`, **hoặc**
  - Đổi healthcheck sang không cần curl, ví dụ dùng chính runtime:
    `test: ["CMD", "dotnet", "--info"]` (chỉ chứng minh container sống) — nhưng tốt nhất vẫn là cài curl để
    check đúng `/api/health`.

---

## 2 việc OCI dễ quên
1. **OCI Security List / NSG** (firewall tầng cloud): thêm **Ingress TCP 80 + 443** cho `0.0.0.0/0`.
   Thiếu bước này thì container chạy nhưng ngoài Internet không vào được.
2. **iptables mặc định của image OCI** thường chặn sẵn — nếu mở Security List rồi vẫn không thông,
   kiểm tra `sudo iptables -L` và cho phép 80/443 (hoặc dùng `ufw` nhất quán).
