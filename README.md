# SocialApp — Mạng xã hội (Facebook-like)

Backend ASP.NET Core 8 (modular monolith) + PostgreSQL 16, theo bản PTTK & Kế hoạch triển khai.

## Cấu trúc repo
```
mxh/
├─ SocialApp.sln                     # solution (tạo ở GĐ0 — Khởi tạo & Walking Skeleton)
├─ Dockerfile                        # build backend, context = gốc repo
├─ .dockerignore / .gitignore
├─ .github/workflows/                # CD lên staging
├─ src/
│  ├─ SocialApp.Api                  # host: controllers, SignalR Hubs, DI, middleware
│  ├─ SocialApp.SharedKernel         # AuthN/AuthZ, RFC7807, correlation ID, rate limit
│  └─ Modules/                       # 7 module — mỗi module tách Domain/Application/Infrastructure
│     ├─ Identity                    # users, roles, refresh_tokens, JWT
│     ├─ Profile                     # profiles
│     ├─ SocialGraph                 # friendships, follows
│     ├─ Content                     # posts, comments, reactions, media, feed
│     ├─ Messaging                   # conversations, messages, ChatHub
│     ├─ Notification                # notifications, Hub
│     └─ Moderation                  # reports, audit_logs, admin
├─ tests/
│  ├─ SocialApp.UnitTests
│  ├─ SocialApp.IntegrationTests
│  ├─ SocialApp.ArchitectureTests    # ArchUnitNET chặn tham chiếu chéo module
│  └─ load/                          # k6 (feed @1.000 CCU)
├─ deploy/                           # compose dev + staging (2 biến thể: caddy / apache), không chứa .env
└─ frontend/                         # Next.js 14 (làm sau khi backend ổn định)
```

## Quy ước
- REST `/api/v1`, lỗi RFC 7807, cursor pagination (limit 20, max 50).
- JWT 15' + refresh rotation; RBAC 3 tầng (AuthN → role → ownership); default deny.
- UUID v7 cho PK; `created_at/updated_at` chuẩn; mọi thao tác Mod/Admin ghi `audit_logs`.
- Module chỉ giao tiếp qua interface ở tầng Application (ArchUnitNET chặn tham chiếu chéo).

## Trạng thái hiện tại
**GĐ0 + GĐ0B xong.** Walking Skeleton chạy thật trên Internet: `https://mxh.banhgao.net`
(`/api/v1/ping`, `/health/ready`, `/swagger`). VM OCI (arm64) chạy apache reverse proxy
path-based (`/api`,`/swagger`,`/health` → API; `/` để dành cho Next.js sau).

**CD tự động (không còn deploy tay):** merge vào `develop` → GitHub Actions build image
`linux/arm64` → push ghcr → SSH vào VM chạy `docker-compose.staging.apache.yml`
(`run --rm migrate` → `up -d --remove-orphans`). Script deploy có `set -e` nên deploy hỏng thì
CD báo **đỏ** — tránh lặp lại sự cố CD báo xanh trong khi staging đã sập.

Bước tiếp theo: **GĐ1 — Identity & Access** (đăng ký/verify email, JWT + refresh rotation, RBAC 3 tầng).
Lộ trình đầy đủ GĐ0→GĐ8: xem `docs/ke-hoach-trien-khai.md`.

## Tài liệu (trong repo)
- [`docs/ke-hoach-trien-khai.md`](docs/ke-hoach-trien-khai.md) — lộ trình build 8 giai đoạn (thứ tự chính thức).
- [`docs/oci-setup.md`](docs/oci-setup.md) — hạ tầng staging OCI + Cloudflare R2 + Caddy TLS + CD.
- [`AGENTS.md`](AGENTS.md) — hướng dẫn cho AI agent (tech stack, kiến trúc, luật vàng).
