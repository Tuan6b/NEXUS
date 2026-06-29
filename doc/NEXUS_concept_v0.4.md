# NEXUS

**Local Multi-Agent Coding Orchestration**

> *Một control tower. Nhiều agent. Không hỗn loạn.*

| | |
|---|---|
| **Version** | 0.4 — Agent Management & Storage finalized |
| **Status** | 🟢 Concept đầy đủ — song song viết SRS |
| **Author** | Đoàn Hà Anh Tuấn |
| **Reviews** | Vòng 1 (Architecture) ✅ · Vòng 2 (Protocol) ✅ · Storage + Agent Mgmt ✅ |
| **Doc type** | Vision / Concept — KHÔNG phải SRS/SDS (xem §15) |

---

## TL;DR

NEXUS là một **desktop app (.NET / Avalonia)** đóng vai control tower điều phối nhiều AI coding agent (Claude Code, agy, OpenCode) chạy song song trên cùng một codebase. Dev gõ yêu cầu vào app → app dùng **Claude Code CLI headless** làm coordinator để chia task → spawn các agent CLI one-shot làm từng module trong git worktree cách ly → agents báo cáo realtime về app qua **MCP** → dev review và approve ngay trong app.

**3 nguyên tắc thiết kế xuyên suốt:**
1. **Xài quota đã mua, không gọi API** — mọi LLM call đi qua CLI headless (Plan Pro/free tier), không tốn token API.
2. **Xoá vấn đề thay vì thêm cơ chế** — không mock, không git-merge, không agent-to-agent chat, không LLM trong loop điều phối.
3. **Đúng đắn > tận dụng tối đa** — agent chờ dependency (IDLE) là bình thường; tin test/compiler chứ không tin lời agent.

---

## 1. Vấn đề đang giải quyết

Hiện tại khi dùng AI coding agent, dev làm việc với **từng agent riêng lẻ**. Muốn làm feature phức tạp nhiều module → tự chia task thủ công, tự copy context giữa các terminal, tự merge kết quả. Không có gì kết nối chúng lại.

**Cụ thể:**
- Không có cơ chế phân chia file ownership → 2 agent dễ đụng nhau (race condition trên file).
- Context agent này không liên quan agent kia → mỗi thằng đọc lại toàn bộ codebase → tốn token.
- Không có state chung → không biết thằng kia làm gì, xong chưa.
- Dùng model mạnh cho mọi việc → đắt không cần thiết.
- Không có gì giám sát → agent chết giữa chừng (crash/quota) thì mất việc âm thầm.

---

## 2. Ý tưởng cốt lõi

Một **app GUI trung tâm** vừa là coordinator, vừa là dashboard giám sát. Bên trong app host một **MCP server** để agents kết nối vào báo cáo. Dev điều khiển toàn bộ từ app — gõ task, xem tiến độ realtime, bấm nút điều khiển, approve kết quả.

```
Dev gõ task trong app
   → App spawn Claude Code headless (coordinator) → chia task + gen test
   → App spawn các agent CLI one-shot (agy/opencode) kèm task
   → Agents làm trong git worktree riêng, báo cáo về app qua MCP
   → App verify (compile + test), hiển thị realtime, dev review + approve
   → App gộp kết quả vào repo thật (có guard chống đè code dev)
```

---

## 3. Kiến trúc tổng quan

```
┌─────────────────── NEXUS.exe (Avalonia .NET) ──────────────────┐
│  ┌─ Task Input ──────────────────────────────────────────┐    │
│  │ > Làm tính năng đặt vé combo...                        │    │
│  └───────────────────────────────────────────────────────┘    │
│  ┌─ Coordinator (spawn Claude Code headless) ────────────┐    │
│  │  claude -p "decompose..." → StdoutSanitizer → JSON     │    │
│  │  (dùng quota Plan Pro, KHÔNG gọi API)                  │    │
│  └───────────────────────────────────────────────────────┘    │
│  ┌─ Agent Status ──────┐  ┌─ Task Board (kanban) ────────┐    │
│  │ ● auth    🟢 ALIVE  │  │ PENDING │ RUNNING │ DONE      │    │
│  │ ● booking 🟢 ALIVE  │  │ promo   │ auth 60%│ payment ✅│    │
│  │ ● promo   🔴 DISCONN│  └──────────────────────────────┘    │
│  └─────────────────────┘                                       │
│  ┌─ Activity Log ──┐  ┌─ Review [Approve][Fix][Reject] ─┐     │
│  └─────────────────┘  └─────────────────────────────────┘     │
│  ┌─ Backend ────────────────────────────────────────────┐     │
│  │ MCP Server → Channels → BackgroundService            │     │
│  │   → SQLite (Dapper) → SignalR push UI                 │     │
│  │ Watchdog: heartbeat + orphan detect + retry          │     │
│  │ ShadowRepo: worktree + scope validate + dev-guard    │     │
│  └──────────────────────────────────────────────────────┘     │
└────────────────────┬───────────────────────────────────────────┘
        spawn (Process.Start + Job Object)  │ MCP (báo cáo ngược)
   ┌──────────────────┼───────────┼──────────────────┐
   ▼                  ▼           ▼                  ▼
Claude Code      agy run      opencode run      opencode run
(coordinator)    (agent)      --agent build     --agent build
headless         worktree:auth worktree:booking  worktree:promo
```

**2 luồng tách biệt:**
- **Spawn** (app → agent): `Process.Start` chạy CLI headless kèm task, bọc trong Job Object để diệt zombie.
- **MCP** (agent → app): agent connect ngược vào MCP server trong app để báo cáo.

---

## 4. Các quyết định thiết kế (đã chốt qua review)

### 4.1 Coordinator = một role qua Agent Adapter (mặc định Claude Code, KHÔNG khoá cứng)

Coordinator là phần "thông minh nhất" (đọc skill, phân rã, sinh interface + test) nhưng KHÔNG khoá cứng vào Claude — nó là một **role** đóng qua Agent Adapter, dev chọn nguồn được:
- **Mặc định:** Claude Code headless (`claude -p`) — chất lượng phân rã tốt nhất, cần Plan Pro.
- **Free:** opencode + DeepSeek V3 — chạy không tốn xu nếu prompt đủ chặt.
- **Local:** lmstudio / Ollama — offline hoàn toàn.

> **Vì sao ưu tiên CLI, hạn chế API:** Claude Code (và các CLI tương tự) KHÔNG chỉ là API call — nó là cả hệ coding agent xây quanh model (agentic loop, tool use, context management, MCP client sẵn). Gọi API thuần = vứt bỏ toàn bộ phần đó, phải tự build lại. Dùng CLI = thừa hưởng hệ agentic người ta đã làm + dùng quota sub đã mua. Vẫn giữ option API cho dev có sẵn key của hãng.

> **Trade-off cảnh báo dev:** coordinator yếu → phân rã sai từ gốc → cả run hỏng (khác worker rẻ chỉ hỏng 1 module). Hệ thống cảnh báo khi coordinator dùng model dưới ngưỡng đề xuất. Nhưng các lớp bảo vệ (compile+test gate, StdoutSanitizer, cycle-detect) vẫn gánh đỡ để coordinator free không làm sập hệ thống.

App .NET vẫn là orchestrator thuần — KHÔNG chứa LLM. Riêng nhánh API thì App giữ key (qua OS keychain, không plaintext).

### 4.2 Agent từ 3 nguồn, đều qua Agent Adapter, quản trong GUI

```
CLI-based (ưu tiên):  opencode, agy, claude-code, qwencode
   → hưởng agentic system, dùng sub/free tier
Local-model:          lmstudio, ollama
   → model local, offline, không tốn xu
API-based (option):   OpenAI / Anthropic / Google key
   → cho dev có sẵn credit, không có CLI sub
```

- Worker agent = **one-shot** (làm xong 1 task → exit).
- **opencode**: `opencode run --agent build "<task>"`. ⚠️ KHÔNG dùng `serve` + `run --attach` (bug #13851).
- Cả 3 nguồn chui qua chung **Agent Adapter** (§4.9) — chỉ khác implement bên trong.
- **Quản lý agent ngay trong GUI:** add / detect đã cài chưa / tải CLI xuống nếu thiếu / cấu hình / enable-disable / remove. Dev không cần ra terminal để setup.

### 4.3 File Ownership Lock qua Profile YAML
Mỗi agent chỉ đọc/ghi file trong `scope.owns`. Validate trước khi gộp; touch file ngoài scope → REJECT.

### 4.4 Agents không bao giờ thấy nhau
Chỉ giao tiếp gián tiếp qua MCP (publish/read contract). Không agent nào đọc output/conversation của agent khác → loại bỏ group-chat loop tận gốc.

### 4.5 Chờ dependency thì IDLE, KHÔNG mock
Cần interface chưa có → task PENDING, agent IDLE chờ. IDLE (chờ, hệ thống vẫn tiến) ≠ DEADLOCK (A↔B chờ nhau). Đúng đắn > tận dụng tối đa.

### 4.6 Contract-First + Test-First (chống lệch interface VÀ logic rỗng)
**Nâng cấp sau review.** Coordinator generate từ skill YAML:
1. **Interface file** thật (Java interface) → agent `implements` → compiler catch lệch signature.
2. **Unit test** định nghĩa hành vi đúng → agent code phải làm test XANH, **không được sửa test**.

→ Compiler bắt sai *type*, test bắt sai *behavior*. Diệt luôn case "agent viết `return null;` vẫn compile pass".

> **Vì sao coordinator gen test, không phải agent:** nếu agent tự gen test cho mình, nó gen test dễ dãi để pass. Tách người-gen-test (coordinator) khỏi người-code (agent) như TDD pipeline.

### 4.7 Skeleton over Spec
Coordinator gen sẵn class skeleton (signature + comment TODO). Model rẻ fill logic, không tự suy luận architecture. Chất lượng nằm ở coordinator (Claude), không ở agent.

### 4.8 Verify bằng compiler + test, không tin self-report
`report_done` PHẢI kèm `javac` **và** `mvn test` (test của coordinator). Cả 2 xanh mới pass. Fail → task quay lại RUNNING. Tin compiler + test, không tin lời agent.

### 4.9 Agent Adapter — trừu tượng hoá mọi loại agent

Hỗ trợ nhiều CLI khác nhau (opencode/agy/claude-code, tương lai qwencode/lmstudio) + 3 nguồn (CLI/local/API) → KHÔNG hardcode vào core. Mỗi loại agent = 1 adapter implement chung interface. Adapter lo cả **vòng đời**, không chỉ spawn:

```yaml
adapter: opencode
source_type: cli                    # cli | local-model | api
detect:
  command: "opencode --version"     # check đã cài chưa
install:                            # cách tải nếu thiếu (per-OS)
  windows: "npm install -g opencode-ai"
  linux:   "npm install -g opencode-ai"
  macos:   "npm install -g opencode-ai"
  download_size: "~40MB"
  source: "npm (official)"          # minh bạch nguồn — KHÔNG host binary lạ
spawn:
  command: "opencode run --agent build --model {model}"
roles: [worker, coordinator]        # đóng được vai nào
```

**Thêm agent type mới = viết 1 adapter, không sửa core** (extensibility). Download CLI phải **confirm + minh bạch nguồn/size**, KHÔNG cài ngầm.

### 4.10 Storage — SQLite (live) + JSON (history)

Dùng đúng tool cho đúng access pattern:

```
Live state (hot)  →  SQLite (.nexus/state.db)
   • task đang chạy, agent status, heartbeat, contract
   • cần query dependency graph, atomic update, crash recovery
   • SQLite EMBEDDED — KHÔNG phải server như MSSQL,
     nhúng thẳng vào app, user không cài gì, portable như 1 file

Run history (cold)  →  JSON (.nexus/history/run-*.json)
   • run đã xong, diff, log, quyết định approve/reject
   • human-readable, portable, share được (như LM Studio, mỗi run 1 file)
   • write-once khi run xong → không lo concurrent
```

Nhờ §10 (mọi MCP event qua Channel → 1 BackgroundService consumer) → chỉ **1 writer** ghi SQLite → không lo corruption dù nhiều agent.

---

## 5. Stdout Parsing — StdoutSanitizer (sạn 1, sau review)

**Vấn đề:** CLI nhả stdout lẫn ANSI escape codes, "checking for updates...", warning lôm côm trước/sau JSON. Parse thẳng = crash.

**Giải pháp 3 lớp (an toàn tuyệt đối):**
```
1. Strip ANSI:    regex \x1B\[[0-9;]*m → bỏ màu mè terminal
2. Delimiter:     ép Claude wrap JSON giữa <<<JSON>>> và <<<END>>>
                  → app chỉ cắt giữa 2 mốc, không đoán
3. Brace-count:   nếu thiếu delimiter, fallback đếm { } cân bằng
                  (KHÔNG dùng regex greedy — sai với nested braces)
```

Delimiter là lớp chính (chắc nhất vì không phụ thuộc đoán), brace-count là phao cứu sinh.

---

## 6. Git Strategy — Shadow Repo + Dev-Edit Guard

### 6.1 Shadow Repo (gộp không-merge)
```
project/            ← Repo THẬT (push GitHub, lịch sử sạch)
.nexus/
  shadow.git/       ← bare, local-only, KHÔNG push
  worktrees/<agent>/← agent làm việc (git worktree)
```
Ownership disjoint → "gộp" = union diff không giao nhau. Validate `touched_files ⊆ scope.owns` trước khi copy vào `project/`. Coordinator commit 1 lần → lịch sử dev sạch.

### 6.2 Dev-Edit Guard (sạn 3, sau review)
**Vấn đề:** dev sửa `project/` thật lúc agent đang cày → gộp về đè mất code dev.

**Giải pháp:**
```
Trước spawn:  base_hash = git rev-parse HEAD (project thật)
Lúc gộp:      current_hash = git rev-parse HEAD
   nếu current == base    → an toàn, copy + commit
   nếu khác (dev đã sửa):
       ├─ dev sửa file KHÁC file agent gộp → gộp được, cảnh báo nhẹ
       └─ dev sửa ĐÚNG file tranh chấp → STOP, ném UI:
          "Bạn đã sửa X đang được agent xử lý.
           [Giữ bản bạn] [Lấy bản agent] [Xem diff cả 2]"
```
Phân biệt file tranh chấp vs file vô can → tránh báo động giả.

---

## 7. Fault Tolerance (agent disconnect / hết quota)

**Lớp 1 — Heartbeat:** agent gọi `heartbeat(task_id)` mỗi 30s. Watchdog quét mỗi 60s; `last_seen > 90s` + RUNNING → ORPHANED. Heartbeat feed cả watchdog lẫn UI connection status.

**Lớp 2 — Checkpoint:** agent `git commit` WIP định kỳ trong worktree. ORPHANED → app đọc commit cuối → không làm lại từ 0.

**Lớp 3 — Requeue + Retry:**
```
ORPHANED → retry < 3?  CÓ → PENDING + resume_hint
                       KHÔNG → ESCALATED → báo dev UI
```

**Hết quota:** detect 429 / `report_failed(reason=QUOTA)` → failover sang `fallback_models` trong profile (retry cùng model vô nghĩa).

---

## 8. Task State Machine

```
   App tạo ──► PENDING ◄──────────────────────┐
                 │ spawn one-shot kèm task     │
                 ▼                             │
              RUNNING ──progress%──► UI update  │
              │  │  │                           │
   verify ok  │  │  └─heartbeat ngừng>90s──► ORPHANED
   (compile+  │  │                              │ respawn(retry<3)
    test)     │  └─report_failed──► FAILED      │ + resume_hint
              ▼                       │         │
            DONE              retry<3?└──────────┘
                                │ retry>=3
                                ▼
                            ESCALATED → báo dev UI
```

---

## 9. MCP Tool Schema

One-shot (task kèm lúc spawn) → MCP không cần task queue. Chỉ còn nhóm báo cáo ngược:

```yaml
register(agent_id, role, profile_path)    # connect, app đọc profile
heartbeat(task_id)                        # còn sống — watchdog + UI
report_progress(task_id, percent, note)   # cập nhật %, UI realtime
publish_contract(module, method, sig)     # expose interface đã xong
read_contract(module)                     # đọc interface module khác
report_done(task_id, diff_path)           # xong → app verify compile+test
report_failed(task_id, error_log, reason) # lỗi (compile/test/quota/timeout)
```

**MCP chuẩn** (nuget `ModelContextProtocol`) → agent nào cũng connect, kể cả Claude Code.

---

## 10. Backend Pipeline — Channels + BackgroundService (OQ-10)

**Chống race trên UI:** KHÔNG để MCP tool call gọi thẳng SignalR broadcast (block thread + state sai thứ tự).

```
MCP tool nhận data (Producer)
   → quăng Event vào Channel
       ├─ channel_critical (done/failed/contract) — bounded, KHÔNG drop
       └─ channel_progress (%/heartbeat) — bounded + DropOldest khi nghẽn
   → BackgroundService (Consumer) đọc tuần tự
       → update SQLite (Dapper)
       → push SignalR lên UI
```
Tách 2 channel: event quan trọng không bao giờ mất, event spam (%) drop được khi nghẽn để khỏi phình RAM.

---

## 11. Spawn Lifecycle trên .NET (OQ-12)

**Diệt zombie process khi app tắt:**
```
Windows:  Job Objects (nuget ChildProcessTracker)
          → app chết → Windows tự clear toàn bộ process con trong Job
Linux/Mac: lưu PID list + AppDomain.ProcessExit handler
          → CẢNH BÁO: ProcessExit KHÔNG chạy khi bị SIGKILL (kill -9)
          → chắc ăn hơn: P/Invoke prctl(PR_SET_PDEATHSIG)
            → con tự chết khi cha chết, không phụ thuộc handler
```
**Đọc stdout:** `Process.BeginOutputReadLine()` async event — KHÔNG bao giờ block UI thread.

---

## 12. Coordinator Prompt Template (OQ-11)

Ép Claude nhả JSON chuẩn + dễ cắt (ghép với StdoutSanitizer §5):
```
You are an orchestration router. Decompose the user request into
disjoint tasks. Output ONLY valid JSON matching this schema:
{ tasks: [{ agent_type, task_desc, owns_files, depends_on }] }.
Wrap the JSON between <<<JSON>>> and <<<END>>> markers.
No explanations, no markdown blocks, no greetings.
```

---

## 13. Resume Hint Format (OQ-13)

One-shot respawn không có context thằng trước → hint phải self-contained:
```yaml
resume_hint:
  original_task: "Implement createComboBooking theo IBookingService"
  contract:    "<path interface file phải implements>"
  test_file:   "<test phải làm xanh>"
  work_done:   "<git diff HEAD~1 — code thằng trước viết được>"
  death_reason:"Compile fail dòng 45: cannot find symbol 'getEmail'"
  hint:        "Đã có khung, thiếu xử lý null + sai tên method getEmail"
```
`death_reason` chỉ thẳng chỗ vỡ → agent mới + Ollama đọc log là nhảy vào fix ngay.

---

## 14. Tech Stack

| Layer | Chọn | Lý do |
|-------|------|-------|
| App / UI | **Avalonia (.NET)** | Cross-platform, RAM nhẹ (~20–50MB) |
| Realtime UI | **SignalR + Channels** | Push state, tuần tự, không race |
| Live state | **SQLite + Dapper (embedded)** | Query + atomic + crash-recovery; nhúng sẵn, KHÔNG phải server như MSSQL |
| Run history | **JSON (1 file/run)** | Human-readable, portable, share được (như LM Studio) |
| MCP server | **ModelContextProtocol (C#)** | MCP chuẩn |
| Coordinator | **Agent Adapter** (mặc định Claude Code, mở opencode/lmstudio/API) | Quota sub, không khoá cứng 1 hãng |
| Agents | **Agent Adapter**: CLI (agy/opencode/claude) + local (lmstudio/ollama) + API | 3 nguồn, quản trong GUI |
| Agent install | **Detect + download trong GUI** (npm/official release) | Setup không cần terminal, có confirm |
| API key | **OS keychain** (Win Cred Mgr / macOS Keychain / Secret Service) | KHÔNG plaintext |
| Spawn | **Process + Job Object / PDEATHSIG** | Chạy CLI, diệt zombie |
| Git | **Shadow Repo + worktree** | Gộp không-merge, checkpoint free |
| Parse | **StdoutSanitizer** | ANSI strip + delimiter + brace-count |

---

## 15. Lộ trình Tài liệu hoá (Documentation Roadmap)

> *Phần này phục vụ cả project lẫn môn SWR302. Document hiện tại (v0.3) là **Vision/Concept** — chưa phải SRS/SDS. Đây là bản đồ từ concept → tài liệu chuẩn quy trình phần mềm.*

### 3 tầng tài liệu

| Tài liệu | Trả lời | Trạng thái |
|---|---|---|
| **Vision/Concept** (v0.1→v0.3) | Làm cái gì & tại sao? | ✅ Xong |
| **SRS** (Software Requirements Spec) | Hệ thống PHẢI làm được gì? | ⬜ Bước tiếp |
| **SDS** (Software Design Spec) | Làm NHƯ THẾ NÀO? | ⬜ Sau SRS |

### Cần thu thập gì để viết SRS

SRS chuẩn (theo IEEE 830 / mẫu FPT) cần các phần sau. Đánh dấu cái nào v0.3 đã có nguyên liệu, cái nào phải đi thu thập thêm:

```
1. Introduction
   - Purpose, Scope                    → ✅ có (§1, §2)
   - Definitions, Acronyms             → ⬜ cần lập glossary (MCP, agent, coordinator, worktree...)

2. Overall Description
   - Product Perspective               → ✅ có (§3 kiến trúc)
   - Product Functions                 → ✅ có (rải rác §4-13, cần gom)
   - User Characteristics (Actors)     → ⬜ THIẾU — cần định nghĩa: Dev, Coordinator(system), Agent(system)
   - Constraints                       → 🟡 một phần (cross-platform, không API)
   - Assumptions & Dependencies        → 🟡 một phần (có Plan Pro, có agy/opencode cài sẵn)

3. Functional Requirements (FR)        → ⬜ THIẾU dạng chuẩn — cần viết FR-01, FR-02...
   ví dụ rút từ concept:
     FR-01 Hệ thống phải cho dev nhập task bằng ngôn ngữ tự nhiên
     FR-02 Hệ thống phải decompose task thành sub-task disjoint
     FR-03 Hệ thống phải hiển thị trạng thái kết nối agent realtime
     FR-04 Hệ thống phải phát hiện agent disconnect trong ≤90s
     FR-05 Hệ thống phải verify code bằng compile + test trước khi pass
     ... (bóc tiếp từ §4-13)

4. Non-Functional Requirements (NFR)   → ⬜ THIẾU phần lớn — cần thu thập:
     NFR Performance:  app idle RAM ≤ ? MB; UI update latency ≤ ? ms
     NFR Reliability:  retry tối đa mấy lần; recover sau crash thế nào
     NFR Usability:    dev không cần biết orchestration vẫn dùng được
     NFR Portability:  chạy Win/Linux/Mac
     NFR Security:     (defer multi-user, ghi rõ giả định single-user)

5. Use Case (kèm diagram)              → ⬜ THIẾU — cần vẽ:
     UC-01 Submit task
     UC-02 Monitor agents
     UC-03 Review & approve
     UC-04 Handle agent failure
     UC-05 Resolve dev-edit conflict

6. External Interface Requirements     → 🟡 có nguyên liệu (MCP, CLI, SignalR)
```

### Cần gì để viết SDS (sau SRS)

```
- Architecture Design        → ✅ phần lớn đã có (§3, §10, §11)
- Class Diagram              → ⬜ cần vẽ (SpawnManager, Watchdog, ShadowRepo, McpServer, TaskRepository...)
- Sequence Diagram           → ⬜ cần vẽ cho từng UC (vd: submit task → spawn → report → merge)
- Database Design (ERD)      → ⬜ cần thiết kế bảng: tasks, contracts, agents, retries
- State Machine Diagram      → ✅ có (§8) — chuẩn hoá lại theo UML
- Component Diagram          → 🟡 có khung repo (§ cũ), cần format UML
```

### Thứ tự đề xuất (vừa làm project vừa nộp môn)

```
Bước 1: Glossary + Actor definition       (nhẹ, làm trước cho rõ thuật ngữ)
Bước 2: Bóc Functional Requirements        (gom từ §4-13 thành FR-xx)
Bước 3: Thu thập Non-Functional Req         (cần đo/giả định vài con số)
Bước 4: Vẽ Use Case + UC specification      (dùng được cho cả môn SWR)
Bước 5: Ráp thành SRS hoàn chỉnh            (theo template FPT)
Bước 6: Từ SRS → SDS (class/sequence/ERD)
Bước 7: Mới bắt đầu code (Vòng 4 build)
```

---

## 16. Review Roadmap

```
[✅] Vòng 1 — Architecture       9/9 OQ đóng
[✅] Vòng 2 — Protocol design    OQ-10→13 đóng + 3 sạn xử lý
        (StdoutSanitizer, Test-gated done, Dev-edit guard)
[ ]  Vòng 3 — Cost & model
        - Quota Pro đủ cho coordinator load?
        - Ollama reviewer model nào?
        - mvn test overhead chấp nhận được?
[ ]  Vòng 4 — Build plan
        - Sequence: MCP core → spawn manager → UI shell
          → coordinator integration → agent integration
        - Test plan từng component

>>> XEN GIỮA Vòng 2 và Vòng 3: làm SRS + SDS (§15) <<<
    Vì project lớn, nên có tài liệu requirement đầy đủ
    trước khi code — đồng thời phục vụ môn SWR302.
```

---

## 17. Khung repo GitHub

```
nexus/
├── README.md                  # pitch + demo gif + quickstart
├── LICENSE                    # MIT
├── docs/
│   ├── 01-vision.md           # = document này (v0.3)
│   ├── 02-srs.md              # Software Requirements Spec (sắp viết)
│   ├── 03-sds.md              # Software Design Spec
│   ├── protocol.md            # MCP tool spec
│   └── profiles.md            # cách viết agent profile
├── src/
│   ├── Nexus.App/             # Avalonia UI (Views, ViewModels)
│   ├── Nexus.Core/            # Coordinator, SpawnManager, Watchdog, ShadowRepo
│   ├── Nexus.Mcp/             # MCP server
│   └── Nexus.State/           # SQLite + Dapper
├── profiles/                  # ví dụ profile YAML
└── samples/                   # demo project (vd MoviesTheater) để test
```

**Pitch README:**
> NEXUS — orchestrate Claude Code, agy, and OpenCode as a coordinated team on one codebase, from a single lightweight desktop app. No API costs, no merge conflicts, no agents talking past each other.

---

*Living document. v0.3 = Concept finalized. Bước tiếp: SRS (§15).*
*Không code cho đến khi SRS + SDS xong và Vòng 4 build plan cleared.*
