# AGENTS.md — Hướng dẫn cho AI agent làm việc trên dự án SocialApp

> Đọc file này trước khi sửa code. Nó tóm tắt bối cảnh, kiến trúc, quy ước và các **luật vàng**.
> Nguồn sự thật chi tiết: bản PTTK (báo cáo A&D), `docs/ke-hoach-trien-khai.md` (lộ trình build 8 giai
> đoạn), `docs/oci-setup.md` (hạ tầng). Nếu code lệch tài liệu → sửa docs trong **cùng commit**.

---

## 1. Dự án là gì
Mạng xã hội kiểu Facebook (quy mô nhỏ, đồ án), **triển khai thật lên Internet**. Chức năng: đăng ký/đăng
nhập, hồ sơ, đăng bài (text + ảnh) theo quyền riêng tư, News Feed, bình luận 3 cấp + cảm xúc, kết
bạn/theo dõi, nhắn tin 1-1 realtime, thông báo, báo cáo & kiểm duyệt, quản trị.

**Đội:** 3 người · ~25 ngày · backend là trọng tâm chấm điểm.

## 2. Mục tiêu đo được (đừng làm hỏng các ngưỡng này)
| ID | Mục tiêu | Ngưỡng |
|---|---|---|
| GOAL-01 | News Feed nhanh dưới tải | p95 ≤ 500ms @ 1.000 CCU |
| GOAL-02 | Chat realtime | gửi→nhận ≤ 1s (khi online) |
| GOAL-03 | An toàn tài nguyên | **0 lỗ hổng IDOR** |
| GOAL-04 | Chạy thật ổn định | uptime ≥ 99%/tháng |
| GOAL-05 | Bảo vệ PII (NĐ 13/2023) | xóa tài khoản + ẩn danh PII |

## 3. Tech stack (cố định — không tự đổi)
- **Backend:** ASP.NET Core 8 (Web API + SignalR), EF Core + Npgsql, modular monolith.
- **DB:** PostgreSQL 16. **Cache/backplane/presence:** Redis 7.
- **Object storage:** Cloudflare R2 (S3-compatible) — local dev dùng MinIO. Ảnh upload/serve qua
  **pre-signed URL**, KHÔNG đi qua API.
- **Auth:** JWT (HS256, access 15') + refresh token **rotation** (lưu băm). RBAC + ownership.
- **Frontend:** Next.js 14 (App Router, TS, Tailwind) — `frontend/`.
- **Hạ tầng:** Docker Compose, Caddy (TLS) sau Cloudflare, VPS OCI Ampere A1 (**ARM64** — image phải arm64).
- **Quan sát:** Serilog (JSON + correlation ID), Prometheus + Grafana, Uptime Kuma.

## 4. Cấu trúc thư mục
```
mxh/
├─ SocialApp.sln
├─ Dockerfile                 # build backend, context = gốc repo, ra SocialApp.Api.dll (arm64, non-root, có curl)
├─ .github/workflows/         # CD build→push GHCR→SSH deploy staging (nhánh develop)
├─ src/
│  ├─ SocialApp.Api           # HOST: controllers, SignalR Hubs, DI, middleware, Program.cs
│  ├─ SocialApp.SharedKernel  # AuthN/AuthZ, RFC7807, correlation ID, rate limit, result types
│  └─ Modules/                # 7 module, mỗi module = Domain / Application / Infrastructure
│     ├─ Identity  Profile  SocialGraph  Content  Messaging  Notification  Moderation
├─ tests/  (UnitTests · IntegrationTests · ArchitectureTests[ArchUnitNET] · load[k6])
├─ deploy/   # docker-compose.staging.yml, Caddyfile (KHÔNG chứa .env)
└─ frontend/ # Next.js 14
```

## 5. Kiến trúc & ranh giới (bắt buộc tuân thủ)
- **Modular monolith** (ADR-001): deploy 1 khối, nhưng module tách bạch.
- **3 tầng mỗi module:** `Domain` (entity + business rule) → `Application` (service + DTO + validator +
  interface) → `Infrastructure` (EF repository).
- **Module KHÔNG tham chiếu chéo trực tiếp** — chỉ giao tiếp qua interface ở tầng Application (vd
  `IAreFriendsQuery` do SocialGraph export cho Content/Messaging dùng). **ArchUnitNET test chặn vi phạm** →
  đừng thêm project reference chéo.
- **SharedKernel** chứa hạ tầng dùng chung (auth middleware, error model, correlation ID, rate limit).

## 6. Module ↔ chức năng ↔ FR
| Module | API | Chức năng | FR |
|---|---|---|---|
| Identity | `/api/v1/auth/*` | đăng ký, xác minh email, login, JWT, refresh, RBAC | FR-001..003 |
| Profile | `/api/v1/users/*` | hồ sơ, avatar | FR-013 |
| SocialGraph | `/api/v1/friends/*`, `/follows/*` | kết bạn, theo dõi | FR-010..012 |
| Content | `/api/v1/posts/*`, `/feed` | bài, bình luận, cảm xúc, media, feed | FR-004..009 |
| Messaging | `/api/v1/conversations/*`, `ChatHub` | chat 1-1 realtime | FR-015..016 |
| Notification | `/api/v1/notifications/*`, Hub | thông báo (gộp cùng loại) | FR-018 |
| Moderation | `/api/v1/reports/*`, `/admin/*` | báo cáo, kiểm duyệt, admin, audit | FR-019..020 |

## 7. Domain model (13 entity chính — xem ERD/PTTK là schema source of truth)
`users`(+`profiles`, `roles`, `permissions`, `role_permissions`, `refresh_tokens`), `posts`,
`comments`(self-FK ≤3 cấp), `reactions`(đa hình post/comment), `media_attachments`(→R2),
`friendships`(PK cặp user_min<user_max), `follows`, `conversations`(UQ cặp), `messages`(seq +
client_msg_id khử trùng), `notifications`(UQ recipient+group_key), `reports`, `audit_logs`(append-only).
- PK = **UUID v7** (tối ưu sắp feed). Cột chuẩn `created_at/updated_at`; xóa mềm bằng `DeletedAt` +
  global query filter.
- **KHÔNG tự bịa schema** — lấy field/kiểu từ ERD trong PTTK (Mục 5.5) trước khi tạo migration.

## 8. Business rules (kiểm bằng test)
- **BR-01** bài: ≤5000 ký tự hoặc ≥1 ảnh; ≤10 ảnh; ≤10MB/ảnh.
- **BR-02** quyền xem: public / friends / private — đánh giá **tại thời điểm đọc**.
- **BR-03** 1 quan hệ bạn/cặp; không tự kết bạn (CHECK user_min<user_max).
- **BR-05** 1 cảm xúc/người/đối tượng; thả loại khác = thay thế.
- **BR-06** chỉ 2 thành viên hội thoại đọc/gửi.
- **BR-07** nội dung bị gỡ → `Hidden`: tác giả thấy kèm lý do, người khác không thấy.
- **BR-08** bình luận tối đa 3 cấp; xóa giữ nhánh ("Bình luận đã bị xóa").
- **BR-09** chỉ bạn bè mới chat; hủy kết bạn → hội thoại chỉ đọc (thay tính năng chặn).

## 9. Quy ước API
- REST, tiền tố `/api/v1`; JSON; Bearer JWT; OpenAPI/Swagger (chỉ bật ở Development).
- Lỗi theo **RFC 7807 Problem Details** `{type,title,status,errors,traceId}`.
- Phân trang **keyset/cursor** (limit 20, tối đa 50), sort ổn định `(created_at, id)` — không OFFSET.
- Rate limit 100 req/phút/user (10 cho auth). Idempotency: PUT reaction, message theo `client_msg_id`.
- `JsonStringEnumConverter` + `UnmappedMemberHandling = Disallow` (gửi field lạ → 400).

## 10. Bảo mật (ưu tiên số 1 — GOAL-03)
**3 tầng kiểm soát mỗi request** (thiếu tầng 3 = IDOR):
1. **AuthN** — verify chữ ký JWT (không query DB).
2. **RBAC** — policy theo role, đọc quyền từ bảng `permissions`/`role_permissions` (data-driven), **default deny**.
3. **Ownership/quan hệ** — kiểm ở tầng Application, có query dữ liệu thật (bài của mình? thành viên hội thoại? là bạn?).

- Mật khẩu: **BCrypt cost ≥ 12**, không lưu plaintext. Access token 15', refresh rotation + reuse
  detection. Đăng xuất/đổi mật khẩu thu hồi phiên.
- Roles: User / Moderator / Admin. Ma trận quyền `resource.action` (vd `post.hide`, `report.resolve`,
  `user.lock`, `role.assign`, `audit.read`) — **thêm vai trò = thêm dữ liệu, không sửa code**.
- Mọi thao tác Mod/Admin ghi `audit_logs` (append-only).
- **Bộ test negative-authz TC-A01..A07 chạy CI** (401/403/truy cập chéo) — phải xanh trước khi merge.
- **Secrets chỉ nằm trong `.env` trên server** (chmod 600, .gitignore). KHÔNG commit, KHÔNG nhúng image.

## 11. Phạm vi tính năng
**In scope (MVP):** 6 UC lõi (UC-02 login, UC-04 đăng bài, UC-08 feed, UC-10/11 kết bạn, UC-15 chat,
UC-19 kiểm duyệt) + UC-01/03/05/06/07/09/13/16/17/18/20.
**Out of scope:** chặn người dùng (thay bằng BR-09), nhóm/trang, chia sẻ lại, chat nhóm, gọi video,
i18n, email digest, app mobile, xếp hạng feed theo quan tâm (chỉ sắp theo thời gian).

## 12. Testing
- **Unit** (business rule, validator, JWT/BCrypt) · **Integration** (endpoint + Postgres thật qua
  Testcontainers, ma trận quyền xem BR-02) · **AuthZ matrix** (IDOR, CI gate) · **E2E** (chat realtime,
  đăng bài→feed) · **Load k6** (feed @1.000 CCU) · **ArchUnitNET** (ranh giới module).
- Coverage ≥ 70% tầng nghiệp vụ; build+test CI ≤ 10 phút.

## 13. Build / Run / Deploy
```bash
# Backend
dotnet build SocialApp.sln
dotnet test
dotnet run --project src/SocialApp.Api        # dev

# Local infra (compose dev — Postgres/Redis/MinIO/Mailpit): sẽ tạo ở deploy/ hoặc gốc
docker compose -f docker-compose.dev.yml up -d

# Frontend
cd frontend && npm run dev
```
- **Migration:** EF Core, versioned, expand–contract (backward-compatible 1 phiên bản). **KHÔNG
  auto-migrate lúc app start** — chạy ở bước deploy (service `migrate`, cờ `--migrate`).
- **CD:** push `develop` → GitHub Actions build arm64 → GHCR → SSH deploy staging. Chi tiết
  `docs/oci-setup.md`.

## 14. LUẬT VÀNG cho agent
1. **Không tự bịa schema/kiến trúc** — schema từ ERD/PTTK, quy ước từ file này. Chỉ tự quyết chi tiết hiện thực.
2. **Mọi endpoint (trừ public) qua đủ 3 tầng authz** + có test IDOR. Đây là rủi ro Critical của dự án.
3. **Không commit secret**; giá trị thật chỉ trong `.env`. Nếu thấy secret trong code/doc → cảnh báo + thay placeholder.
4. **Không tạo tham chiếu chéo giữa module** — đi qua interface Application (ArchUnitNET sẽ chặn).
5. **Lỗi luôn theo RFC 7807**; validate đầu vào; đúng vai trò + đúng chủ sở hữu.
6. **Ảnh: pre-signed URL thẳng lên R2**, không stream qua API; kiểm magic bytes + ≤10MB.
7. **Docs sống cùng code** — lệch thì sửa cùng commit; cập nhật trạng thái trong `README.md`.
8. **ARM64:** mọi image/dependency phải chạy được trên arm64 (OCI Ampere).
9. Ưu tiên **tái sử dụng** hàm/tiện ích có sẵn trước khi viết mới.

## 15. Nguồn tài liệu (source of truth)
- **PTTK / báo cáo A&D** — yêu cầu, UC, FR/NFR, ERD, ma trận RBAC, ADR, threat model.
- **docs/ke-hoach-trien-khai.md** — lộ trình build **8 giai đoạn** (GĐ0→GĐ8) ánh xạ GOAL/NFR, kèm
  "làm gì → làm như nào → kiểm tra lại ra sao" từng giai đoạn. **Đây là thứ tự build chính thức.**
- **docs/oci-setup.md** — hạ tầng OCI, Cloudflare R2, Caddy TLS, CD.

> Lưu ý: `ROADMAP.md` (bản cũ, tên `SocialMedia`/`socialmedia_api`) **KHÔNG dùng nữa** — đã thay bằng
> kế hoạch 8 giai đoạn ở trên (khớp cấu trúc `SocialApp` của repo này).
