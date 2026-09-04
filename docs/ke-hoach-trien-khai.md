# Kế hoạch triển khai (build) Mạng xã hội Facebook-like — Nhóm 5

## Context

Tài liệu PTTK (`BaoCao_Nhom4_v5.pdf`, bản v5.0) đã chốt toàn bộ phân tích & thiết kế: 6 UC lõi
(UC-02 đăng nhập, UC-04 đăng bài, UC-08 feed, UC-10/11 kết bạn, UC-15 chat realtime, UC-19 kiểm
duyệt), 13 entity, ma trận RBAC, 20 FR + 12 NFR, và các ADR (modular monolith, JWT rotation,
SignalR+Redis, fan-out-on-read). Thư mục hiện tại **chưa có mã nguồn ứng dụng** — chỉ có bộ công cụ
sinh tài liệu (pandoc/plantuml) và các file báo cáo `.docx`.

Mục tiêu của kế hoạch này: **hiện thực hóa toàn bộ MVP** đúng theo thiết kế — code, test, triển khai
thật lên OCI sau Cloudflare, và kiểm chứng được các mục tiêu GOAL-01..05 (feed p95 ≤ 500ms, chat
≤ 1s, 0 lỗ hổng IDOR, uptime ≥ 99%, tuân thủ NĐ 13/2023). Ràng buộc: ASP.NET Core 8 + PostgreSQL 16,
đội 3 người, ~25 ngày.

Nguyên tắc xuyên suốt: mỗi bước đều có **"làm gì → làm như nào → kiểm tra lại ra sao"**; mỗi tính năng
chỉ "xong" khi đạt Definition of Done (Mục 3.5 tài liệu): đủ AC + kiểm tra RBAC/ownership + validation
RFC 7807 + chạy thử staging + cập nhật Swagger.

---

## 0. Chuẩn bị & quy ước (áp dụng cả dự án)

**Tech stack cố định:** ASP.NET Core 8 (Web API + SignalR), EF Core + Npgsql, PostgreSQL 16, Redis 7,
Cloudflare R2 (S3-compatible), Docker Compose, Caddy (TLS), Next.js 14 (frontend), Serilog +
Prometheus + Grafana + Uptime Kuma.

**Cấu trúc solution (modular monolith — theo Mục 6.4, 7 module + Shared Kernel):**
```
SocialApp.sln
 ├─ src/
 │   ├─ SocialApp.Api                (host: controllers, SignalR Hubs, DI, middleware)
 │   ├─ SocialApp.SharedKernel       (AuthN/AuthZ, RFC7807, correlation ID, rate limit, result types)
 │   ├─ Modules/Identity             (CMP-01: users, roles, refresh_tokens, JWT)
 │   ├─ Modules/Profile              (CMP-02: profiles)
 │   ├─ Modules/SocialGraph          (CMP-03: friendships, follows)
 │   ├─ Modules/Content              (CMP-04: posts, comments, reactions, media, feed)
 │   ├─ Modules/Messaging            (CMP-05: conversations, messages, ChatHub)
 │   ├─ Modules/Notification         (CMP-06: notifications, Hub)
 │   └─ Modules/Moderation           (CMP-07: reports, audit_logs, admin)
 ├─ tests/  (Unit, Integration, Architecture[ArchUnitNET], Load[k6])
 ├─ deploy/ (docker-compose.*.yml, Caddyfile, prometheus.yml, grafana/)
 └─ frontend/ (Next.js 14)
```
Mỗi module: `Domain` (entity + business rule) / `Application` (service + DTO + validator) /
`Infrastructure` (EF repository). Module chỉ giao tiếp qua interface ở Application — ArchUnitNET
chặn tham chiếu chéo (ADR-001).

**Quy ước chung:** REST `/api/v1`, lỗi RFC 7807 Problem Details, cursor pagination (limit 20, max 50),
rate limit 100 req/phút/user (10 cho auth), UUID v7 cho PK, `created_at/updated_at` chuẩn, mọi thao
tác Mod/Admin ghi `audit_logs`.

**Kiểm tra GĐ0:** `docker compose up` chạy được API + Postgres + Redis + Mailpit; `GET /health/ready`
trả 200; CI (build + test) xanh; ArchUnitNET test khung chạy được (dù chưa có rule vi phạm).

---

## 0B. Thiết lập SERVER thật & CD sớm (Ngày 2–3) — làm ngay trước khi code nhiều

> Mục tiêu: có **môi trường staging thật trên Internet** từ rất sớm để mỗi giai đoạn sau đều
> deploy liên tục (CD thật) và test bằng tài khoản thật — thay vì dồn deploy về cuối.

- **Làm gì:** Provision VPS OCI, hardening, cài Docker, gắn domain + Cloudflare + TLS, tạo R2 bucket,
  cấu hình secrets, và dựng pipeline CD tự động deploy nhánh `develop` lên staging.
- **Làm như nào (từng bước hạ tầng):**
  1. **Tạo VPS:** OCI Ampere A1 (2 OCPU/12GB), Ubuntu LTS; gán public IP; tạo user non-root.
  2. **SSH & firewall hardening:** SSH key-only (tắt password), fail2ban; OCI Security List +
     `ufw` chỉ mở **80/443** (và 22 giới hạn IP) — ISS-04.
  3. **Cài Docker + Compose plugin;** tạo thư mục `deploy/`, mạng nội bộ Docker (zone app/data).
  4. **Domain + Cloudflare:** trỏ DNS về IP, bật proxy (TLS + chống DDoS); Caddy làm reverse proxy
     TLS phía trong (Cloudflare Full/Strict).
  5. **Object Storage:** tạo Cloudflare R2 bucket (`-dev`, `-staging`), cấu hình **CORS** cho
     pre-signed PUT (ISS-02); tạo API token R2 (scope tối thiểu).
  6. **Secrets:** đưa vào CI protected variables (JWT key, DB pass, R2 keys, SMTP) — **không** nhúng
     secret vào image; server đọc qua biến môi trường.
  7. **CD pipeline:** GitHub Actions/GitLab CI: build image → push registry → SSH deploy lên staging
     (`docker compose -f docker-compose.staging.yml up -d`), rolling sau health check.
  8. **compose staging:** Caddy (TLS) → API → Postgres + Redis; volume dữ liệu; healthcheck từng service.
- **Kiểm tra:**
  - `https://<domain>` trả trang/`GET /health/ready` = 200 qua Cloudflare (chứng nhận TLS hợp lệ).
  - `nmap`/kiểm tra port: chỉ 80/443 mở ra ngoài; DB/Redis KHÔNG expose public.
  - Push 1 commit lên `develop` → CD tự deploy → phiên bản mới lên staging không cần thao tác tay.
  - Test pre-signed PUT 1 ảnh lên R2 từ trình duyệt (CORS OK).
  - SSH bằng password bị từ chối; chỉ key mới vào được.

---

## Lộ trình theo giai đoạn (8 giai đoạn / ~25 ngày)

> Mỗi giai đoạn ghi rõ 3 phần: **Làm gì** · **Làm như nào** · **Kiểm tra lại ra sao**.

### GĐ 0 — Khởi tạo & Walking Skeleton (Ngày 1–2)
- **Làm gì:** Dựng khung solution, hạ tầng local, CI/CD, health check, error model, correlation ID,
  Swagger. Tạo 1 endpoint mẫu `GET /api/v1/ping` đi hết pipeline.
- **Làm như nào:**
  - `dotnet new` solution + các project module rỗng; thêm Serilog, Swashbuckle, EF Core, Npgsql,
    StackExchange.Redis.
  - `deploy/docker-compose.dev.yml`: Postgres 16, Redis 7, Mailpit, API. `.env` mẫu (không commit secret).
  - Middleware SharedKernel: exception → RFC 7807, correlation ID header, rate limit (fixed window).
  - GitHub Actions/GitLab CI: restore → build → test → (staging) deploy. ArchUnitNET project khởi tạo.
- **Kiểm tra:** compose up OK; `/health/live` + `/health/ready` (check DB+Redis) = 200; Swagger UI mở
  được; CI pipeline chạy xanh; gọi endpoint lỗi → nhận đúng JSON Problem Details có `traceId`.

### GĐ 1 — Identity & Access: UC-01, UC-02 (Ngày 3–5)
- **Làm gì:** Đăng ký + xác minh email (FR-001), đăng nhập cấp JWT + refresh rotation (FR-002),
  lockout 5 lần/15 phút (FR-003), RBAC 3 tầng (Mục 6.7.1), seed roles/permissions (ENT-10/10a/10b).
- **Làm như nào:**
  - Entity: `users`, `roles`, `permissions`, `role_permissions`, `refresh_tokens` (bảng schema Mục 5.5)
    qua EF Core migration; seed roles (User/Mod/Admin) + ma trận permission (Mục 6.7.2) idempotent.
  - BCrypt cost 12 (NFR-SEC-01); JWT HS256, access 15 phút, claims `sub/role/iat/exp/jti`; refresh
    lưu **băm** trong DB, xoay vòng + reuse detection (ADR-002).
  - Email verify qua Mailpit (dev); `IHostedService`/`IEmailSender` adapter.
  - AuthZ: JWT middleware (AuthN) + policy RBAC (đọc quyền từ DB, cache) + ownership check ở service.
  - Endpoints: `POST /auth/register|login|refresh`, `POST /auth/verify-email`.
- **Kiểm tra:** map thẳng AC US-002 & test 401/403 (Mục 6.7.5):
  - AC-01 login đúng → 200 + cặp token; AC-02 sai mật khẩu → 401 không lộ email + `failed_login_count++`;
  - AC-03 sai 5 lần → 423 Locked 15 phút; AC-04 chưa verify → 403.
  - TC-A01 (không JWT → 401), TC-A02 (token hết hạn/chữ ký sai → 401).
  - refresh reuse → thu hồi cả chuỗi. Integration test trên Postgres thật (Testcontainers).

### GĐ 2 — Profile + Content (đăng/sửa/xóa bài + ảnh): UC-03, UC-04, UC-05 (Ngày 5–8)
- **Làm gì:** Hồ sơ + avatar (FR-013), đăng bài văn bản + ≤10 ảnh với privacy (FR-004, BR-01/02),
  sửa/xóa mềm bài (FR-005), upload ảnh qua pre-signed URL thẳng lên R2 (không qua API).
- **Làm như nào:**
  - Entity `profiles`, `posts`, `comments`(khung), `reactions`(khung), `media_attachments`.
  - Luồng SEQ-01: client xin pre-signed URL (hạn 10 phút) → PUT ảnh thẳng R2 → `POST /posts`
    validate BR-01 (≤5000 ký tự hoặc ≥1 ảnh; ≤10 ảnh; ≤10MB/ảnh) → lưu post+media trong 1 transaction
    → phát event `PostCreated`.
  - CHECK constraint privacy (public/friends/private) + status (published/hidden/deleted).
  - Background worker dọn media mồ côi (upload dở).
- **Kiểm tra:** AC US-004: AC-01 đăng công khai → 201; AC-02 bài rỗng không ảnh → 400 (BR-01);
  AC-03 chọn 11 ảnh → 400; AC-04 JWT hết hạn → 401. TC-A03 (User A `PATCH /posts/{id của B}` → 403 IDOR).
  Test upload R2 (dev bucket) + rollback khi lỗi ghi DB (ảnh mồ côi được job dọn).

### GĐ 3 — Tương tác: bình luận 3 cấp + cảm xúc: UC-06, UC-07 (Ngày 8–10)
- **Làm gì:** Bình luận ≤1000 ký tự, trả lời tối đa 3 cấp, xóa giữ nhánh (FR-007, BR-08);
  thả/đổi/gỡ 1 cảm xúc/đối tượng + cập nhật bộ đếm (FR-008, BR-05).
- **Làm như nào:**
  - `comments.parent_id` self-FK + CHECK ≤3 cấp; status visible/deleted → hiển thị "Bình luận đã bị xóa".
  - `reactions` PK (user, target_type, target_id); `PUT /reactions` idempotent, đổi loại thì thay thế;
    cập nhật `posts.reaction_counts` (jsonb) + `comment_count` cùng transaction.
  - API-Comment (401/403 theo BR-02 quyền xem bài), API-Reaction (PUT idempotent).
- **Kiểm tra:** test 3 cấp comment (cấp 4 bị chặn); thả 2 loại cảm xúc liên tiếp → chỉ còn 1 (BR-05);
  bộ đếm khớp bản ghi thật; bình luận trên bài không có quyền xem → 403.

### GĐ 4 — Social Graph + News Feed: UC-10/11, UC-13, UC-08 (Ngày 10–14) ⚠️ trọng điểm hiệu năng
- **Làm gì:** Kết bạn Pending→Accepted (FR-010/011, BR-03), theo dõi 1 chiều (FR-012),
  News Feed fan-out-on-read + cache Redis (FR-009, BR-02/07, ADR-004).
- **Làm như nào:**
  - `friendships` PK(user_min,user_max) + CHECK user_min<user_max + requester_id; `follows` PK cặp.
  - Feed (SEQ-03): lấy danh sách bạn+following (cache Redis 60s) → cache trang đầu TTL 30s → miss thì
    query index `idx(author_id, created_at DESC) WHERE published` → lọc BR-02 (quyền riêng tư) + loại
    Hidden BR-07 → trả 20 bài + cursor. Degrade khi Redis chết (đọc thẳng DB); DB timeout >5s → 503.
  - Cursor keyset theo (created_at, id), không OFFSET.
- **Kiểm tra:** AC US-010 (AC-01 accept → hai bên là bạn; AC-02 gửi trùng → 409; AC-03 tự gửi → 400;
  AC-04 người thứ 3 accept → 403). AC US-008 (AC-01 20 bài mới nhất + cursor; AC-02 bài "bạn bè" của
  người lạ KHÔNG hiện; AC-03 bài Hidden không hiện). **k6 load test feed @1.000 CCU → p95 ≤ 500ms**
  (NFR-PERF-01) — mốc kiểm chứng GOAL-01, chạy lại cuối GĐ8 sau tối ưu index.

### GĐ 5 — Nhắn tin 1-1 realtime: UC-15 (Ngày 14–18) ⚠️ trọng điểm realtime
- **Làm gì:** Chat 1-1 realtime ≤1s, trạng thái Sent→Delivered→Seen, chỉ 2 thành viên & chỉ bạn bè
  (FR-015/016, BR-06/09), idempotency chống trùng tin.
- **Làm như nào:**
  - `conversations` UQ(a,b)+CHECK a<b+seq_counter; `messages` UQ(conv,seq)+UQ(conv,client_msg_id).
  - SignalR `ChatHub` + Redis backplane (ADR-003); SEQ-02: SendMessage → validate JWT+BR-06+BR-09 →
    INSERT (message+seq+last_message atomically) → ACK Sent → tra presence Redis → đẩy B → Delivered/Seen.
  - B offline → tăng badge chưa đọc + tạo notification; mất WebSocket → fallback REST
    `POST /conversations/{id}/messages`; retry cùng `client_msg_id` khử trùng.
- **Kiểm tra:** AC US-015 (AC-01 B online nhận ≤1s + đủ trạng thái; AC-02 B offline → badge khi online;
  AC-03 retry cùng clientMsgId không trùng; AC-04 không phải bạn → 403 hội thoại chỉ đọc).
  TC-A04 (đọc conversation không phải thành viên → 403), TC-A07 (gửi tin cho người lạ → 403).
  **E2E 2 trình duyệt đo p95 gửi→nhận ≤ 1s** (NFR-PERF-03, GOAL-02).

### GĐ 6 — Notification + Search + Moderation/Admin: UC-16,17,18,19,20 (Ngày 18–20)
- **Làm gì:** Thông báo (comment/reaction/tag/friend/message) gộp cùng loại (FR-018); tìm người dùng
  không dấu tiền tố (FR-017); báo cáo nội dung (FR-019); kiểm duyệt ẩn/gỡ + audit (FR-020, BR-07);
  admin khóa/mở tài khoản + gán vai trò.
- **Làm như nào:**
  - `notifications` UQ(recipient, group_key) để gộp; đẩy realtime qua Hub.
  - Search: GIN pg_trgm trên `unaccent(display_name)`, prefix match, q≥2 ký tự.
  - Moderation (UC-19): `post.status=Hidden` + `report=Resolved` + ghi `audit_log` cùng transaction;
    A1 không vi phạm → Dismissed. Admin endpoints ghi audit.
- **Kiểm tra:** AC US-019 (AC-01 ẩn → Hidden+Resolved+audit; AC-02 bỏ qua → Dismissed; AC-03 user
  thường gọi endpoint kiểm duyệt → 403 default deny + audit; AC-04 xử lý lại → 409). TC-A05
  (user gọi `/admin/*` → 403), TC-A06 (user gọi `PATCH /reports/{id}` → 403). Tìm "nguyen" khớp
  "Nguyễn"; thông báo cùng loại được gộp.

### GĐ 7 — Lên PRODUCTION + Observability + Backup/DR (Ngày 20–22)
- **Làm gì:** Nâng staging (đã dựng ở GĐ0B) lên **production** đầy đủ HA, giám sát và sao lưu (GOAL-04).
  *(Hạ tầng nền — VPS, Docker, Cloudflare, TLS, R2, CD — đã có từ GĐ0B; GĐ này tập trung production-grade.)*
- **Làm như nào (Mục 6.3/6.8/6.9):**
  - Nhân bản compose production: Caddy (TLS) → **2 API container** (stateless) → Postgres/Redis; deploy theo tag.
  - Serilog JSON + correlation ID (redact PII); Prometheus (RED metrics + business metrics) + Grafana; Uptime Kuma → `/health`.
  - Backup: `pg_basebackup` + WAL archiving (RPO ≤ 15 phút); **thực hiện 1 lần restore drill** có biên bản.
  - Rolling từng container sau health check; migration expand–contract (backward-compatible 1 phiên bản); rollback theo image tag.
- **Kiểm tra:** truy cập domain HTTPS thật; Caddy loại instance fail (kill 1 container vẫn phục vụ);
  Grafana hiển thị metrics; alert error rate >1%/5 phút; **restore drill có biên bản** (NFR-REL-02);
  Uptime Kuma theo dõi ≥ 99%.

### GĐ 8 — Kiểm chứng NFR + Security + Hardening + Bàn giao (Ngày 22–25)
- **Làm gì:** Chạy đủ Verification Plan (Mục 7.2), vá lỗi, hoàn tất tài liệu & sign-off.
- **Làm như nào:**
  - k6 feed @1.000 CCU (NFR-PERF-01) — báo cáo p95 + Grafana; nếu vượt 400–500ms → thêm index/cache
    (ADR-004 review), cân nhắc hybrid.
  - AuthZ matrix test tự động TC-A01..A07 chạy trên CI (NFR-SEC-02) — mục tiêu **0 lỗ hổng IDOR** (GOAL-03).
  - Security scan ZAP + SQLi/XSS, TLS 1.2+ (NFR-SEC-04); code review BCrypt/JWT.
  - NĐ 13/2023: quyền xóa tài khoản → vô hiệu hóa ngay + ẩn danh PII sau 30 ngày; audit_logs giữ 12 tháng
    (NFR-COMP-01) — inspection.
  - Coverage ≥ 70% tầng nghiệp vụ; build+test CI ≤ 10 phút (NFR-MAINT).
- **Kiểm tra:** RTM (Mục 7.1) đi xuôi & ngược — mỗi GOAL có đường hiện thực + test; checklist review
  Mục 8.1–8.3 tick đủ; điền Sign-off 8.4.

---

## Chiến lược test (xuyên suốt)
- **Unit:** business rule/domain (BR-01..09), validator, JWT/BCrypt.
- **Integration:** endpoint + Postgres thật (Testcontainers), ma trận quyền xem BR-02.
- **AuthZ matrix (CI gate):** TC-A01..A07 — chặn IDOR trước khi merge.
- **E2E:** realtime chat (2 client), luồng đăng bài → feed.
- **Load (k6):** feed @1.000 CCU; báo cáo p95/p99/error rate + Grafana.
- **Architecture (ArchUnitNET):** chặn tham chiếu chéo module.
- **API (Postman/newman):** FR-004..020 xanh trên CI.

## Rủi ro cần theo dõi (Mục 7.3)
- ISS-01 SignalR tốn thời gian → fallback polling 3s, giữ nguyên hợp đồng API.
- ISS-02 R2 CORS/pre-signed trục trặc → tạm lưu volume VPS, giữ bảng metadata để chuyển lại R2.
- ISS-04 spam đăng ký → rate limit + lockout + firewall 80/443.

## Thứ tự phụ thuộc (không đảo được)
GĐ0 → **GĐ0B (server + CD sớm, có staging thật)** → GĐ1 (auth là nền) → GĐ2 (content cần auth) →
GĐ4 (feed cần content + social graph) → GĐ5/GĐ6 (song song được sau GĐ4) → GĐ7 (production hardening)
→ GĐ8 (verify). GĐ3 chèn linh hoạt sau GĐ2. Từ GĐ0B trở đi **mỗi giai đoạn đều deploy lên staging thật**.

## Definition of Done cho mỗi UC (Mục 3.5)
Đủ AC · có RBAC + ownership check · validation RFC 7807 · chạy thử staging bằng tài khoản thật ·
cập nhật Swagger · không lộ secret/PII.

---

## Verification tổng (đối chiếu GOAL)
| GOAL | Kiểm chứng | Giai đoạn |
|---|---|---|
| GOAL-01 feed p95 ≤ 500ms | k6 @1.000 CCU | GĐ4 (sơ bộ) → GĐ8 (chính thức) |
| GOAL-02 chat ≤ 1s | E2E 2 trình duyệt | GĐ5 |
| GOAL-03 0 IDOR | AuthZ matrix TC-A01..A07 trên CI | GĐ1,2,5,6 → GĐ8 |
| GOAL-04 uptime ≥ 99% | Uptime Kuma + Caddy failover | GĐ7 |
| GOAL-05 NĐ 13/2023 | Inspection quyền xóa + ẩn danh PII | GĐ8 |

## Đầu ra dạng Artifact
Sau khi duyệt kế hoạch, tôi sẽ render một trang HTML trực quan (timeline 8 giai đoạn + checklist +
bảng verify GOAL) để nhóm theo dõi và chia sẻ.
