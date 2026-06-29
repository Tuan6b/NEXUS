# NEXUS — Software Requirements Specification (SRS)

| | |
|---|---|
| **Document** | SRS — Software Requirements Specification |
| **Version** | 0.2 — Agent Management & Storage added |
| **Project** | NEXUS — Local Multi-Agent Coding Orchestration |
| **Author** | Đoàn Hà Anh Tuấn |
| **Based on** | Vision/Concept v0.4 |
| **Standard** | IEEE 830-style / FPT SRS template |
| **Status** | 🟡 Draft — NFR cần confirm số, UC cần chi tiết hoá |
| **Changelog** | v0.2: +nhóm K (Agent Lifecycle), +nhóm L (Storage), coordinator mở qua adapter, 3 nguồn agent, API key keychain |

---

## 1. Introduction

### 1.1 Purpose
Tài liệu này đặc tả yêu cầu phần mềm cho **NEXUS** — một desktop app điều phối nhiều AI coding agent chạy song song trên cùng một codebase. SRS định nghĩa *hệ thống PHẢI làm được gì* (không mô tả *làm như thế nào* — phần đó thuộc SDS).

Đối tượng đọc: dev (chính tác giả), người chấm môn SWR302, và bất kỳ ai contribute vào repo sau này.

### 1.2 Scope
**Trong phạm vi (MVP):**
- App GUI cross-platform (Windows / Linux / macOS) làm control tower.
- Điều phối agent từ **3 nguồn**: CLI (opencode, agy, Claude Code), local-model (lmstudio, ollama), API (OpenAI/Anthropic/Google) — tất cả qua Agent Adapter.
- **Coordinator mở**: mặc định Claude Code headless, nhưng config được sang opencode/local/API (không khoá cứng 1 hãng).
- Quản lý agent trong GUI: add, detect, download CLI, configure, enable/disable, remove.
- Phân rã task, sinh interface + test, spawn agent, theo dõi realtime, verify, gộp code, review/approve.
- Storage hybrid: SQLite (live state) + JSON (run history).
- Single-user, single-machine.

**Ngoài phạm vi (future / defer):**
- Agent types bổ sung: qwencode (kiến trúc adapter sẵn sàng, chưa implement đủ).
- Multi-user / nhiều dev chung 1 server (security namespace).
- Cloud/remote orchestration.

### 1.3 Definitions, Acronyms & Glossary

| Thuật ngữ | Định nghĩa |
|---|---|
| **Agent** | Một AI coding tool (CLI/local/API) được NEXUS spawn/gọi để code một module. |
| **Coordinator** | Role phân rã task + sinh interface/test. Mặc định Claude Code, config được sang nguồn khác. |
| **Worker Agent** | Agent thực thi code (phân biệt với Coordinator). |
| **Agent Adapter** | Lớp trừu tượng cho mỗi loại agent (detect/install/spawn/parse/kill) để core không phụ thuộc tool cụ thể. |
| **Agent Source** | Nguồn của agent: `cli` / `local-model` / `api`. |
| **MCP** | Model Context Protocol — giao thức agent connect ngược về NEXUS để báo cáo. |
| **Profile** | File YAML định nghĩa identity + scope + model của một agent. |
| **Scope (ownership)** | Tập file một agent được phép đọc/ghi. |
| **Contract** | Method signature (+ test) một module expose cho module khác. |
| **Shadow Repo** | Git repo nội bộ, bare, local-only, sandbox cho agents; tách khỏi repo thật. |
| **Worktree** | Thư mục làm việc git riêng của mỗi agent, cách ly file. |
| **One-shot** | Agent làm xong 1 task rồi exit (không thường trú). |
| **Heartbeat** | Tín hiệu định kỳ agent gửi để chứng tỏ còn sống. |
| **Orphaned task** | Task có agent chết giữa chừng (mất heartbeat). |
| **Resume hint** | Gói context (task gốc + diff + lý do chết) giao cho agent respawn. |
| **Dev-edit guard** | Cơ chế chống gộp đè khi dev sửa repo thật lúc agent đang chạy. |
| **Live state** | Dữ liệu nóng (task đang chạy, agent status) — lưu SQLite. |
| **Run history** | Dữ liệu lạnh (run đã xong, diff, quyết định) — lưu JSON, 1 file/run. |

### 1.4 References
- Vision/Concept v0.4 (docs/01-vision.md)
- IEEE Std 830-1998
- Model Context Protocol specification

---

## 2. Overall Description

### 2.1 Product Perspective
NEXUS là desktop app độc lập, host bên trong một MCP server và state store. Nó **điều khiển các tool agent bên ngoài** (coordinator + workers, từ 3 nguồn) qua spawn/gọi, và **nhận báo cáo ngược** qua MCP. NEXUS không chứa LLM — mọi suy luận LLM ủy thác cho agent bên ngoài để tận dụng hệ agentic + quota đã có. Riêng nhánh API thì App giữ key an toàn qua OS keychain.

### 2.2 System Boundary & Actors

```
              ┌──────────────────────────────────┐
   Developer ─┤                                  ├─ Coordinator (Claude Code / opencode / local / API)
   (human)    │           NEXUS                  ├─ Worker Agent (CLI / local-model / API)
              │  (UI + MCP + State + Logic)       ├─ Git (shadow + project repo)
              │                                  ├─ Package source (npm / GitHub release)
              │                                  ├─ OS Keychain (lưu API key)
              └──────────────────────────────────┘
```

| Actor | Loại | Vai trò |
|---|---|---|
| **Developer** | Primary (human) | Nhập task, theo dõi, review/approve, xử lý conflict, quản agent, cấu hình. |
| **Coordinator** | Secondary (system) | Phân rã task, sinh interface + test. |
| **Worker Agent** | Secondary (system) | Nhận task, code, báo cáo qua MCP. |
| **Git** | Supporting (system) | Quản shadow repo, worktree, commit. |
| **Package source** | Supporting (external) | Nguồn tải CLI (npm/official release) khi dev cài agent trong app. |
| **OS Keychain** | Supporting (system) | Lưu API key an toàn cho nhánh API-based. |

### 2.3 User Characteristics
Developer có kiến thức lập trình, quen dùng AI coding tool, nhưng **không cần hiểu cơ chế orchestration nội bộ** — NEXUS che giấu độ phức tạp. Dev có thể không có Plan Pro → phải dùng được với coordinator free/local.

### 2.4 Constraints
- Coordinator + agents là tool bên ngoài; NEXUS phụ thuộc sự tồn tại + giao diện của chúng (giảm thiểu qua Adapter + chức năng tự cài).
- Ưu tiên CLI/local, hạn chế API (CLI mang theo hệ agentic, không chỉ model).
- Cross-platform bắt buộc → không dùng API chỉ-một-OS mà thiếu fallback.
- Download CLI chỉ từ nguồn chính thống; không host binary lạ.

### 2.5 Assumptions & Dependencies
- Dev **ưu tiên** có Plan Pro (cho coordinator Claude Code) nhưng **không bắt buộc** — chạy được với coordinator free (opencode+DeepSeek) hoặc local (lmstudio/ollama).
- Máy có git; có Node/npm nếu cài agent CLI dạng npm.
- Agent CLI có thể **chưa cài** — NEXUS hỗ trợ detect + tải trong app.
- (Optional) Ollama/LM Studio đã cài nếu dùng nguồn local-model.

---

## 3. Functional Requirements (FR)

> Ký hiệu: **M** = Must (MVP), **S** = Should, **C** = Could.

### A. Task Submission & Coordination
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-01 | Dev nhập task bằng ngôn ngữ tự nhiên qua UI. | M |
| FR-02 | Hệ thống gọi **Coordinator (qua Agent Adapter, mặc định Claude Code, config được sang opencode/local/API)** để phân rã task thành sub-task file-ownership **disjoint**. | M |
| FR-03 | Coordinator sinh **interface contract + unit test** cho mỗi module trước khi giao task. | M |
| FR-04 | Hệ thống **sanitize stdout** coordinator (strip ANSI + cắt delimiter `<<<JSON>>>`, fallback brace-count) trước khi parse JSON. | M |
| FR-05 | Hệ thống build dependency graph + **phát hiện circular dependency** trước khi spawn; có vòng → báo dev, không spawn. | M |
| FR-06 | Hệ thống **cảnh báo dev** khi coordinator dùng model dưới ngưỡng đề xuất (rủi ro phân rã kém). | S |

### B. Agent Adapter & Execution
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-07 | Hệ thống hỗ trợ agent qua **Agent Adapter** cho 3 nguồn: **CLI / local-model / API** (MVP: opencode, agy, Claude Code; +ollama/lmstudio; +API OpenAI/Anthropic/Google). | M |
| FR-08 | Thêm agent type mới = thêm 1 adapter, **không sửa core** (extensibility). | M |
| FR-09 | Mỗi agent load **profile YAML** (scope, model, fallback_models, worktree, source_type). | M |
| FR-10 | Hệ thống spawn worker dạng **one-shot kèm task** qua adapter. | M |
| FR-11 | Agent **connect ngược vào MCP** để báo cáo (register). | M |
| FR-12 | Hệ thống **enforce file ownership**: reject/không gộp nếu agent touch file ngoài `scope.owns`. | M |

### C. Task Execution & Tracking
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-13 | Hệ thống duy trì **task list** (PENDING/RUNNING/DONE/FAILED/ORPHANED/ESCALATED) trong live state. | M |
| FR-14 | Agent báo `progress %`; hệ thống cập nhật UI **realtime**. | M |
| FR-15 | Task có dependency chưa resolved giữ **PENDING** (không spawn) tới khi dependency DONE. | M |

### D. Inter-agent Coordination
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-16 | Agent **publish contract** lên MCP sau khi implement. | M |
| FR-17 | Agent **read contract** module khác qua MCP. | M |
| FR-18 | Agents **không** truy cập output/branch/conversation của agent khác trực tiếp. | M |

### E. Verification & Quality Gate
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-19 | `report_done` trigger **compile (javac) + test (mvn test)** trong worktree. | M |
| FR-20 | Task pass chỉ khi **cả compile + test xanh**; fail → RUNNING lại kèm log. | M |
| FR-21 | Test do Coordinator sinh **không được agent sửa**. | M |
| FR-22 | (Optional) Ollama review local trước khi gộp. | C |

### F. Fault Tolerance
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-23 | Agent gửi **heartbeat** (≤30s); watchdog detect mất (>90s) → ORPHANED. | M |
| FR-24 | Agent **commit WIP** định kỳ trong worktree (checkpoint). | M |
| FR-25 | ORPHANED **respawn với resume_hint** (≤ MAX retry, default 3); ≥ MAX → ESCALATED. | M |
| FR-26 | Hết quota (429 / reason=QUOTA) → **failover** sang `fallback_models`. | M |

### G. Git / Merge / Dev-Guard
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-27 | Init **shadow repo** (bare, local-only) + **worktree per agent**. | M |
| FR-28 | Gộp = **copy file disjoint** vào repo thật + 1 commit (KHÔNG git-merge). | M |
| FR-29 | **Dev-edit guard**: so HEAD hash trước/sau; dev sửa file tranh chấp → cảnh báo, không đè; file vô can → gộp + cảnh báo nhẹ. | M |

### H. Monitoring / Dashboard
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-30 | UI hiển thị **trạng thái kết nối agent** realtime (ALIVE/IDLE/DISCONNECTED + last_seen). | M |
| FR-31 | UI hiển thị **task board** kanban realtime. | M |
| FR-32 | UI hiển thị **activity log**. | S |

### I. Review & Approval
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-33 | UI cho dev xem **diff tổng hợp** + **approve / request-fix / reject**. | M |
| FR-34 | Approve → commit repo thật; reject → discard worktree; request-fix → respawn kèm feedback. | M |

### J. Configuration
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-35 | Dev cấu hình **max concurrent agents** (default 5). | M |
| FR-36 | Dev cấu hình **adapter + model cho coordinator** (+ fallback). | M |
| FR-37 | Dev cấu hình **profile/model per agent type** (+ fallback_models). | S |

### K. Agent Lifecycle Management (GUI) — *mới v0.2*
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-38 | Dev **thêm agent từ GUI** (chọn loại + nguồn), không cần ra terminal. | M |
| FR-39 | Hệ thống **detect** agent CLI đã cài chưa (vd `opencode --version`). | M |
| FR-40 | Nếu CLI chưa cài → hệ thống **đề nghị tải/cài trong app**, hiển thị **nguồn + dung lượng**, và **chờ dev confirm** trước khi tải (không cài ngầm). | M |
| FR-41 | Cài CLI theo **lệnh per-OS** khai báo trong adapter (Win/Linux/macOS). | M |
| FR-42 | Dev **enable / disable / remove** agent đã thêm. | M |
| FR-43 | Với nguồn **API**: dev nhập API key; hệ thống lưu qua **OS keychain** (KHÔNG plaintext). | M |
| FR-44 | Với nguồn **local-model**: dev cấu hình endpoint (lmstudio/ollama local URL). | S |

### L. Storage — *mới v0.2*
| ID | Yêu cầu | ƯT |
|---|---|---|
| FR-45 | Hệ thống persist **live state vào SQLite embedded** (.nexus/state.db); restore khi khởi động (crash recovery). | M |
| FR-46 | Hệ thống export **run đã hoàn tất ra JSON** (.nexus/history/run-*.json), 1 file/run, human-readable. | M |
| FR-47 | Storage **không phụ thuộc DB server ngoài** (không MSSQL/MySQL) — embedded + file. | M |

---

## 4. Non-Functional Requirements (NFR)

> ⚠️ Con số là **đề xuất** — cần confirm/đo ở Vòng 3.

### 4.1 Performance
| ID | Yêu cầu | Mục tiêu |
|---|---|---|
| NFR-P1 | App idle RAM (không tính process agent). | ≤ 150 MB |
| NFR-P2 | Độ trễ cập nhật UI từ MCP event. | ≤ 500 ms |
| NFR-P3 | Overhead spawn 1 agent. | ≤ 3 s |

### 4.2 Reliability
| ID | Yêu cầu |
|---|---|
| NFR-R1 | Recover ORPHANED không mất WIP đã commit. |
| NFR-R2 | App crash → live state đã persist (SQLite); restart đọc lại task dở. |
| NFR-R3 | Watchdog phát hiện agent chết trong ≤ 90 s. |

### 4.3 Usability
| ID | Yêu cầu |
|---|---|
| NFR-U1 | Dev không cần hiểu orchestration nội bộ vẫn dùng được. |
| NFR-U2 | Mọi thao tác chính (submit/review/approve/**add agent**) làm trong app, không cần terminal. |

### 4.4 Portability
| ID | Yêu cầu |
|---|---|
| NFR-Po1 | Chạy được Windows / Linux / macOS (**bắt buộc MVP**). |
| NFR-Po2 | Spawn lifecycle xử lý zombie process trên cả 3 OS. |
| NFR-Po3 | State store **embedded**, không phụ thuộc DB server ngoài. |
| NFR-Po4 | Run history **portable** (1 file JSON/run, copy là share được). |

### 4.5 Extensibility
| ID | Yêu cầu |
|---|---|
| NFR-Ex1 | Thêm agent type/nguồn mới = viết 1 adapter, không sửa core. |
| NFR-Ex2 | Coordinator cũng đóng qua adapter (mở rộng như worker). |
| NFR-Ex3 | Adapter khai báo được detect + install + spawn (full lifecycle). |

### 4.6 Security
| ID | Yêu cầu |
|---|---|
| NFR-S1 | MVP single-user, single-machine — không xử lý multi-user isolation. |
| NFR-S2 | **API key lưu qua OS keychain** (Win Credential Manager / macOS Keychain / Linux Secret Service), KHÔNG plaintext. |
| NFR-S3 | Download CLI chỉ từ nguồn chính thống (npm/official release); minh bạch nguồn + size; confirm trước khi tải. |

---

## 5. Use Cases

### 5.1 Danh sách Use Case
| ID | Use Case | Actor chính |
|---|---|---|
| UC-01 | Submit & decompose task | Developer |
| UC-02 | Execute task | Worker Agent |
| UC-03 | Monitor agents & tasks | Developer |
| UC-04 | Resolve inter-agent contract | Worker Agent |
| UC-05 | Verify & quality gate | Coordinator / System |
| UC-06 | Handle agent failure (orphan/retry/failover) | System |
| UC-07 | Review & approve | Developer |
| UC-08 | Resolve dev-edit conflict | Developer |
| UC-09 | Configure settings | Developer |
| UC-10 | **Add & install agent** (GUI) | Developer |
| UC-11 | **Configure API/local agent** | Developer |

### 5.2 Use Case chi tiết (mẫu) — UC-01: Submit & decompose task

| Mục | Nội dung |
|---|---|
| **ID** | UC-01 |
| **Actor** | Developer (chính), Coordinator (phụ) |
| **Precondition** | App đang chạy; ≥1 agent đã register; repo thật đã mở. |
| **Trigger** | Dev gõ task và submit. |
| **Main flow** | 1. Dev nhập task ngôn ngữ tự nhiên.<br>2. Hệ thống gọi Coordinator (adapter) với prompt template.<br>3. Coordinator trả JSON (qua StdoutSanitizer).<br>4. Build dependency graph, kiểm tra circular.<br>5. Sinh interface + test từng module.<br>6. Tạo task (PENDING) trong live state.<br>7. UI cập nhật task board. |
| **Alternate** | 4a. Circular dependency → báo dev, dừng. |
| **Exception** | 3a. Stdout không parse được JSON sau sanitize → báo lỗi, cho retry.<br>2a. Coordinator model dưới ngưỡng → hiện cảnh báo (FR-06). |
| **Postcondition** | Task ở PENDING, sẵn sàng spawn. |

### 5.3 Use Case chi tiết (mẫu) — UC-10: Add & install agent

| Mục | Nội dung |
|---|---|
| **ID** | UC-10 |
| **Actor** | Developer (chính), Package source (phụ) |
| **Precondition** | App đang chạy. |
| **Trigger** | Dev bấm "Add Agent" trong GUI. |
| **Main flow** | 1. Dev chọn loại agent + nguồn (CLI/local/API).<br>2. Hệ thống detect agent đã cài chưa (FR-39).<br>3. (CLI chưa cài) Hệ thống hiện nguồn + size, hỏi confirm tải.<br>4. Dev confirm → hệ thống chạy lệnh cài per-OS, hiện tiến trình.<br>5. Cài xong → dev cấu hình profile/model.<br>6. Agent thêm vào danh sách, sẵn sàng register. |
| **Alternate** | 1a. Nguồn API → bỏ qua cài, nhập API key (lưu keychain, FR-43).<br>1b. Nguồn local → nhập endpoint (FR-44).<br>2a. Đã cài rồi → nhảy tới bước 5. |
| **Exception** | 4a. Cài thất bại (mạng/quyền) → báo lỗi, hướng dẫn cài thủ công. |
| **Postcondition** | Agent có trong danh sách, enable được. |

> UC-02 → UC-09, UC-11 chi tiết hoá ở vòng tiếp theo (cùng template).

---

## 6. Traceability — FR ↔ Concept

| FR nhóm | Nguồn (Vision v0.4) |
|---|---|
| A (Coordination) | §4.1, §4.6, §5, §12 |
| B (Adapter/Exec) | §4.2, §4.3, §4.9, §9 |
| C (Tracking) | §7, §8 |
| D (Inter-agent) | §4.4, §9 |
| E (Verification) | §4.6, §4.8 |
| F (Fault Tolerance) | §7 |
| G (Git/Guard) | §6 |
| H (Monitoring) | §3, §10 |
| I (Review) | §3, §4 |
| J (Config) | §4.1 (coordinator mở), concurrency |
| K (Agent Lifecycle) | §4.2, §4.9 (detect/install/source) |
| L (Storage) | §4.10 |

---

## 7. Gaps còn lại (cho vòng SRS tiếp theo)

```
[ ] Chi tiết hoá UC-02 → UC-09, UC-11 (cùng template)
[ ] Vẽ Use Case Diagram (UML) — gồm cả actor Package source / Keychain
[ ] Confirm/đo các con số NFR (P1-P3) — Vòng 3
[ ] Quyết định giữ hay bỏ lớp Ollama reviewer (FR-22)
[ ] External Interface Requirements chi tiết:
    - MCP tool I/O schema
    - Adapter spec per agent (detect/install/spawn flags) cho từng CLI
[ ] Chốt danh sách agent + nguồn cài chính thống (npm package name, release URL)
```

---

*SRS v0.2 — thêm Agent Lifecycle Management + Storage + coordinator mở.*
*Bước tiếp: chi tiết hoá Use Case còn lại + Use Case Diagram, rồi sang SDS.*
