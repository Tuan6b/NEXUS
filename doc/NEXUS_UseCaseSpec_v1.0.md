# NEXUS — Use Case Specification

| | |
|---|---|
| **Document** | Use Case Specification |
| **Version** | 1.0 |
| **Project** | NEXUS — Local Multi-Agent Coding Orchestration |
| **Author** | Đoàn Hà Anh Tuấn |
| **Based on** | SRS v0.2 |
| **Note** | Agile — UC sẽ cập nhật theo feedback sau mỗi sprint. |

---

## Actors tổng quan
- **Developer** — người dùng chính (human).
- **Coordinator** — role phân rã task + sinh interface/test (system).
- **Worker Agent** — agent thực thi code (system).
- **Git**, **Package source**, **OS Keychain** — supporting actors.

## Use Case Diagram (mô tả text — vẽ UML sau)
```
Developer ──┬─ UC-01 Submit & decompose task
            ├─ UC-03 Monitor agents & tasks
            ├─ UC-07 Review & approve
            ├─ UC-08 Resolve dev-edit conflict
            ├─ UC-09 Configure settings
            ├─ UC-10 Add & install agent ───────── Package source
            └─ UC-11 Configure API/local agent ─── OS Keychain

Worker Agent ──┬─ UC-02 Execute task
               └─ UC-04 Resolve inter-agent contract

System ──┬─ UC-05 Verify & quality gate ───────── Coordinator
         └─ UC-06 Handle agent failure
```

---

## UC-01 — Submit & decompose task
| Mục | Nội dung |
|---|---|
| **Actor** | Developer (chính), Coordinator (phụ) |
| **Precondition** | App chạy; ≥1 agent register; repo thật mở. |
| **Trigger** | Dev gõ task và submit. |
| **Main flow** | 1. Dev nhập task ngôn ngữ tự nhiên.<br>2. Hệ thống gọi Coordinator (adapter) với prompt template.<br>3. Coordinator trả JSON (qua StdoutSanitizer).<br>4. Build dependency graph, kiểm tra circular.<br>5. Sinh interface + test từng module.<br>6. Tạo task (PENDING) trong live state.<br>7. UI cập nhật task board. |
| **Alternate** | 4a. Circular dependency → báo dev, dừng. |
| **Exception** | 3a. Không parse được JSON sau sanitize → báo lỗi, cho retry.<br>2a. Coordinator model dưới ngưỡng → cảnh báo (FR-06). |
| **Postcondition** | Task ở PENDING, sẵn sàng spawn. |

---

## UC-02 — Execute task
| Mục | Nội dung |
|---|---|
| **Actor** | Worker Agent (chính), System (phụ) |
| **Precondition** | Task PENDING; dependency đã resolved; còn slot (< max concurrent). |
| **Trigger** | System spawn agent cho task PENDING. |
| **Main flow** | 1. System spawn agent one-shot kèm task + interface + test, trong worktree riêng.<br>2. Agent register vào MCP.<br>3. Agent code trong scope.owns của mình.<br>4. Agent gửi heartbeat + report_progress định kỳ.<br>5. Agent commit WIP định kỳ (checkpoint).<br>6. Agent xong → report_done kèm diff.<br>7. System chuyển sang verify (UC-05). |
| **Alternate** | 3a. Agent cần contract module khác → UC-04. |
| **Exception** | E1. Agent mất heartbeat >90s → UC-06.<br>E2. Agent touch file ngoài scope → System reject, task FAILED.<br>E3. report_failed (lỗi/quota) → UC-06. |
| **Postcondition** | Task chuyển verify (DONE-pending) hoặc FAILED/ORPHANED. |

---

## UC-03 — Monitor agents & tasks
| Mục | Nội dung |
|---|---|
| **Actor** | Developer |
| **Precondition** | App chạy. |
| **Trigger** | Dev mở dashboard (mặc định màn chính). |
| **Main flow** | 1. UI hiển thị panel kết nối agent (ALIVE/IDLE/DISCONNECTED + last_seen).<br>2. UI hiển thị task board kanban realtime.<br>3. UI hiển thị activity log.<br>4. State update đẩy qua SignalR → UI re-render realtime. |
| **Alternate** | 1a. Dev click 1 task → xem chi tiết (status, agent, progress, log).<br>2a. Dev lọc theo trạng thái/agent. |
| **Exception** | E1. Backend mất kết nối → UI hiện chỉ báo "stale", không crash. |
| **Postcondition** | Dev thấy trạng thái hiện tại của toàn hệ thống. |

---

## UC-04 — Resolve inter-agent contract
| Mục | Nội dung |
|---|---|
| **Actor** | Worker Agent |
| **Precondition** | Agent đang chạy task có depends_on module khác. |
| **Trigger** | Agent cần gọi method của module khác. |
| **Main flow** | 1. Agent gọi read_contract(module) qua MCP.<br>2. MCP trả signature đã published.<br>3. Agent code dựa trên signature đó.<br>4. Sau khi implement xong phần mình expose → agent gọi publish_contract. |
| **Alternate** | — |
| **Exception** | E1. Contract chưa published (lẽ ra không xảy ra vì dependency resolved trước spawn) → agent báo blocked, System xử lý như dependency chưa sẵn. |
| **Constraint** | Agent KHÔNG được đọc trực tiếp branch/output agent khác — chỉ qua contract (FR-18). |
| **Postcondition** | Contract được consume/publish đúng. |

---

## UC-05 — Verify & quality gate
| Mục | Nội dung |
|---|---|
| **Actor** | System (chính), Coordinator (nguồn test) |
| **Precondition** | Agent đã report_done kèm diff. |
| **Trigger** | report_done nhận được. |
| **Main flow** | 1. System chạy `javac` compile trong worktree.<br>2. Compile pass → chạy `mvn test` (test của Coordinator).<br>3. Cả 2 xanh → đánh dấu task DONE-verified, đủ điều kiện merge. |
| **Alternate** | — |
| **Exception** | E1. Compile fail → task về RUNNING kèm log lỗi → respawn/fix.<br>E2. Test fail → task về RUNNING kèm test report → respawn/fix.<br>E3. Agent cố sửa test của Coordinator → System phát hiện (test file thay đổi) → reject. |
| **Postcondition** | Task DONE-verified, hoặc quay lại RUNNING. |

---

## UC-06 — Handle agent failure
| Mục | Nội dung |
|---|---|
| **Actor** | System |
| **Precondition** | Task RUNNING. |
| **Trigger** | Watchdog detect mất heartbeat >90s, HOẶC report_failed. |
| **Main flow** | 1. System đánh dấu task ORPHANED.<br>2. Đọc WIP commit cuối trong worktree.<br>3. Build resume_hint (task gốc + diff + death_reason).<br>4. retry_count < MAX → respawn one-shot kèm resume_hint → PENDING.<br>5. UI cập nhật trạng thái. |
| **Alternate** | 4a. Lỗi quota (429) → failover sang fallback_models thay vì retry cùng model.<br>4b. retry_count ≥ MAX → ESCALATED, báo dev trên UI. |
| **Exception** | E1. Worktree hỏng/không đọc được WIP → respawn từ đầu (không checkpoint). |
| **Postcondition** | Task respawned (PENDING) hoặc ESCALATED. |

---

## UC-07 — Review & approve
| Mục | Nội dung |
|---|---|
| **Actor** | Developer |
| **Precondition** | ≥1 task DONE-verified. |
| **Trigger** | Dev mở panel review (hoặc tất cả task của run xong). |
| **Main flow** | 1. System tổng hợp diff các agent DONE-verified.<br>2. UI hiển thị summary + diff per file.<br>3. Dev chọn **Approve**.<br>4. System chạy Dev-edit guard (UC-08).<br>5. Không conflict → copy file vào repo thật + 1 commit.<br>6. Export run ra JSON history. |
| **Alternate** | 3a. Dev chọn **Request-fix** → nhập feedback → respawn agent kèm feedback.<br>3b. Dev chọn **Reject** → discard worktree, không gộp. |
| **Exception** | E1. Dev-edit conflict → UC-08 xử lý trước khi gộp. |
| **Postcondition** | Code gộp vào repo thật / yêu cầu fix / bị loại. |

---

## UC-08 — Resolve dev-edit conflict
| Mục | Nội dung |
|---|---|
| **Actor** | Developer |
| **Precondition** | Chuẩn bị gộp; dev đã sửa repo thật từ lúc spawn. |
| **Trigger** | HEAD hash repo thật khác base_hash lúc gộp. |
| **Main flow** | 1. System so HEAD hash hiện tại với base_hash.<br>2. Xác định dev có sửa đúng file agent định gộp không.<br>3. (Đụng file tranh chấp) Hiện UI: [Giữ bản bạn] [Lấy bản agent] [Xem diff cả 2].<br>4. Dev chọn → System áp dụng. |
| **Alternate** | 2a. Dev chỉ sửa file vô can → gộp bình thường, cảnh báo nhẹ. |
| **Exception** | E1. Dev huỷ → không gộp, giữ worktree để xử lý sau. |
| **Postcondition** | Conflict được giải quyết; gộp tiếp hoặc hoãn. |

---

## UC-09 — Configure settings
| Mục | Nội dung |
|---|---|
| **Actor** | Developer |
| **Precondition** | App chạy. |
| **Trigger** | Dev mở Settings. |
| **Main flow** | 1. Dev set max concurrent agents (default 5).<br>2. Dev chọn coordinator: adapter + model + fallback.<br>3. Dev set profile/model per agent type.<br>4. System lưu config. |
| **Alternate** | — |
| **Exception** | E1. Config không hợp lệ (model lạ, adapter chưa cài) → báo validation, không lưu. |
| **Postcondition** | Config lưu, áp dụng cho run kế tiếp. |

---

## UC-10 — Add & install agent
| Mục | Nội dung |
|---|---|
| **Actor** | Developer (chính), Package source (phụ) |
| **Precondition** | App chạy. |
| **Trigger** | Dev bấm "Add Agent". |
| **Main flow** | 1. Dev chọn loại agent + nguồn (CLI/local/API).<br>2. System detect đã cài chưa (FR-39).<br>3. (CLI chưa cài) Hiện nguồn + size, hỏi confirm tải.<br>4. Dev confirm → chạy lệnh cài per-OS, hiện tiến trình.<br>5. Cài xong → dev cấu hình profile/model.<br>6. Agent thêm vào danh sách. |
| **Alternate** | 1a. Nguồn API → UC-11.<br>1b. Nguồn local → UC-11.<br>2a. Đã cài → nhảy bước 5. |
| **Exception** | E1. Cài thất bại (mạng/quyền) → báo lỗi + hướng dẫn cài thủ công. |
| **Postcondition** | Agent có trong danh sách, enable được. |

---

## UC-11 — Configure API/local agent
| Mục | Nội dung |
|---|---|
| **Actor** | Developer (chính), OS Keychain (phụ) |
| **Precondition** | Đang thêm/sửa agent nguồn API hoặc local-model. |
| **Trigger** | Dev chọn nguồn API hoặc local. |
| **Main flow (API)** | 1. Dev nhập API key.<br>2. System lưu key qua **OS Keychain** (KHÔNG plaintext).<br>3. (Optional) Test connection.<br>4. Lưu cấu hình agent. |
| **Main flow (local)** | 1. Dev nhập endpoint URL (lmstudio/ollama).<br>2. System test reachability.<br>3. Lưu cấu hình. |
| **Exception** | E1. Keychain không khả dụng → cảnh báo, không lưu key.<br>E2. Test connection/endpoint fail → cảnh báo, cho sửa. |
| **Postcondition** | Agent API/local cấu hình xong, sẵn sàng dùng. |

---

## Trạng thái & độ phủ
```
UC-01 ✅  UC-02 ✅  UC-03 ✅  UC-04 ✅  UC-05 ✅  UC-06 ✅
UC-07 ✅  UC-08 ✅  UC-09 ✅  UC-10 ✅  UC-11 ✅
```
Tất cả UC đã chi tiết hoá. Bước còn lại: vẽ Use Case Diagram bằng UML tool (draw.io) cho bài SWR.

---
*Agile: cập nhật UC sau mỗi sprint dựa trên feedback thực tế khi build.*
