# Day 2 开发 TODO 清单

面向目标：从“可用调试原型”进化为“稳定、可测试、可扩展”的生产核心。  
今天聚焦：状态机清晰化、Tick 精度、逻辑解耦、存档一致性、基础自动化入口、测试覆盖。

---

## 优先级分层概览

- P0：必须完成（否则后续改动代价快速上升）
- P1：建议完成（提升易用与扩展）
- P2：可选/预埋（为 Day3+ 做准备）

---

## 验收标准（Definition of Done）

- 不再依赖 Progress == 0 / 0.00011 作为“已扣输入”判定。
- 机器新增 Phase（或重构 State），语义自解释：Idle / PendingInput / Processing / OutputReady / BlockedInput / BlockedOutput。
- 长帧（掉帧）不会造成生产速度变慢：Tick 使用 while 消耗累积时间（含补偿）。
- 存档恢复后，正在生产的机器继续运行；若输出已满则正确进入 BlockedOutput；无配方或进度=0 则 Idle/PendingInput。
- Debug 面板改为调用封装 API，不直接写 MachineRuntimeState 内部关键字段。
- 核心推进逻辑提取到纯逻辑类（MachineProcessService），可被单元测试。
- 至少 3 个单元测试通过（输入消耗 / 正常产出 / 输出阻塞）。
- 可切换是否自动将产出收集到玩家背包（AutoCollect）。

---

## P0 任务（必须完成）

### 1. 新增/重构枚举 MachinePhase
- [ ] 创建 MachinePhase 枚举：Idle, PendingInput, Processing, OutputReady, BlockedInput, BlockedOutput
- [ ] MachineRuntimeState 添加 Phase 字段（替换/平行旧 State）
- [ ] 迁移所有引用（Debug UI、保存、日志）

### 2. 重构生产推进逻辑
- [ ] 新建 `MachineProcessService`（纯 C#，无 MonoBehaviour）
  - 方法：Advance(machine, deltaSeconds)
  - TryStart(machine, recipeId)
  - TryCollectOutputs(machine, targetInventory)
  - RebuildStateAfterLoad(machine)
- [ ] 从 `ProductionTickService` 移除内部细节 → 调用 service.Advance
- [ ] 移除 Progress “魔法浮点”哨兵值（0.00011）

### 3. Tick 补偿机制
- [ ] 修改 `ProductionTickService`：`while (_accum >= tickInterval) { _accum -= tickInterval; StepOnce(tickInterval); }`
- [ ] 若将来支持“变速机器”，留接口传入实际 dt

### 4. 存档恢复一致性
- [ ] 在 `MachineRuntimeManager.RebuildFromSnapshot` 后对每台机器调用 `MachineProcessService.RebuildStateAfterLoad`
- [ ] Clamp Progress：`0 <= Progress <= recipe.TimeSeconds`
- [ ] 若 Progress >= recipe.TimeSeconds 且输出缓冲为空 → Phase=OutputReady（或 BlockedOutput 取决于能否放置）
- [ ] 添加版本兼容注释（若配方时长变短）

### 5. 输入/输出逻辑集中
- [ ] 将 HasAllInputs / ConsumeInputs / TryPlaceOutputs 移入 MachineProcessService（或私有静态方法）
- [ ] MachineInventoryBridge 仅负责“从外部库存注入机器输入缓冲”，不再负责配方状态变更
- [ ] StartRecipe 时一次性完成校验+进入 PendingInput

### 6. Debug 面板适配
- [ ] 去掉直接改：ActiveRecipeId / Progress / State
- [ ] 替换为：调用 TryStart / TryCollectOutputs / Refill API
- [ ] UI 根据 Phase 显示状态文本与进度
- [ ] 添加 AutoCollect toggle（绑定 machine.AutoCollectOutputs）

### 7. 单元测试（建议使用 Unity Test Runner 或纯 NUnit）
- [ ] Test_StartRecipe_ConsumesInputsOnce
- [ ] Test_Advance_CompletesProductionAndGeneratesOutputs
- [ ] Test_Advance_BlockedOutputWhenCapacityReached
- [ ] （可选）Test_Restore_RestoresProcessingPhase

### 8. 代码清理 & 统一
- [ ] 删除/标记废弃：旧的 MachineProcessState（或保持到迁移完成再移除）
- [ ] ProductionEvents 添加注释：后续将迁移到 EventBus
- [ ] MachineRuntimeState.ToString 更新为包含 Phase

---

## P1（建议完成）

### 9. 自动产出收集
- [ ] 在 Advance 完成阶段：若 machine.AutoCollectOutputs == true → 尝试直接 Add 到玩家 Inventory → 成功则不进入 BlockedOutput
- [ ] Debug 面板添加勾选框（实时更新）

### 10. 生产统计（基础）
- [ ] 新增 ProductionStats（全局或挂在 manager）
  - Per Recipe: cycles, totalActiveTime, totalBlockedTime
- [ ] 在 Advance 中：不同 Phase 累积对应时间
- [ ] Debug 面板简单显示：本机 CompletedCycles & 平均周期耗时

### 11. 事件安全性增强
- [ ] 封装一个简单 EventBus：Publish/Subscribe，内部 try/catch
- [ ] 将 ProductionEvents 迁移或加一层包装（兼容旧接口）
- [ ] 确保取消订阅不抛异常（弱引用可暂缓）

### 12. 统一输出堆叠策略
- [ ] 定义输出 stack 上限（引用 ItemDefinition.MaxStack 或机器定义的 OutputStackLimit）
- [ ] TryPlaceOutputs 时按上限分裂槽位（如果超出容量 → BlockedOutput）
- [ ] 更新 Debug 面板显示

---

## P2（可选 / 预埋）

### 13. IItemContainer 通用接口
- [ ] 定义接口：CanAdd / Add / CanRemove / Remove / Enumerate
- [ ] InventoryComponent 实现
- [ ] 机器 Input/Output 包一层适配器

### 14. RecipeUnlockService 骨架
- [ ] 数据结构：已解锁 recipeIds HashSet
- [ ] API：IsUnlocked / Unlock / EnumerateUnlocked
- [ ] Debug 面板过滤配方时使用

### 15. Power 占位
- [ ] MachineRuntimeState 增加：bool RequiresPower, bool HasPowerThisTick
- [ ] Advance 前：查询 PowerContext（未实现则默认 true）
- [ ] 若无电：Phase 保持 Processing / Progress 不前进（统计停机时间）

### 16. 批量操作 API 预留
- [ ] MachineRuntimeManager：GetByRecipe / GetByType
- [ ] 批量 StartRecipe / Refill / Collect

---

## 任务执行建议顺序（时间估算参考）

| 顺序 | 任务 | 预估 |是否完成|
|------|------|------|--------------------|
| 1 | 新增 Phase + 修改 RuntimeState | 0.5h |是|
| 2 | MachineProcessService 基础实现 | 1.5h |是|
| 3 | Tick while 补偿改造 | 0.3h |是|
| 4 | 重写 Start / Advance / PlaceOutputs | 0.8h |是|
| 5 | 存档恢复 & RebuildStateAfterLoad | 0.7h |是|
| 6 | Debug 面板适配 | 0.8h |是|
| 8 | 自动收集（P1） | 0.5h |否|
| 9 | 生产统计骨架 | 0.6h |否|
| 10 | 事件封装（可延后） | 0.6h |否|

（合计约 8–9h，可按实际复杂度浮动）

---

## 代码改动建议（最小提交切分）

- Commit 1: Add MachinePhase enum & extend MachineRuntimeState
- Commit 2: Introduce MachineProcessService (skeleton, no usage)
- Commit 3: Refactor ProductionTickService to use new service
- Commit 4: Migrate input/output logic into service
- Commit 5: Adjust save/load (RebuildStateAfterLoad) + tests for restore
- Commit 6: Update Debug panel to API-based interactions
- Commit 7: Add auto-collect + stats (if time)
- Commit 8: Add tests for blocked output / normal completion
- Commit 9: Cleanup & remove legacy code paths

---

## 风险与回退策略

| 风险 | 影响 | 缓解 |
|------|------|------|
| Phase 引入后旧 UI/逻辑未全部迁移 | 运行时状态混乱 | 保留旧字段到最后一刻，添加临时映射 |
| Tick 补偿 while 循环导致性能抖动 | 帧耗升高 | 限制循环次数（如 >5 次则合并剩余时间） |
| 存档中途结构升级 | 旧档加载失败 | version 字段 + 兼容分支 + 日志警告 |
| 输出堆叠逻辑改变 | 旧机器出现 BlockedOutput | 临时允许超量（打警告）后再强制执行 |

---

## 快速检查清单 (提交前)

- [ ] 所有机器在未设置配方时 Phase=Idle
- [ ] 设置配方后 Phase=PendingInput（若缺料：BlockedInput）
- [ ] 产出满时进入 BlockedOutput，清空后回 PendingInput 或 Idle（可配置）
- [ ] AutoCollect 打开时，没有残留 OutputBuffer
- [ ] 掉帧 (模拟 Time.deltaTime=0.6) 后产出时间正确
- [ ] 单元测试全部通过
- [ ] 无直接 UI 改 MachineRuntimeState 内部字段
- [ ] 日志中无频繁异常

---

## 后续（Day 3 展望，供参考）

- 物流/传送：IItemContainer + Pull/Push 回调
- 电力：按总功率预算动态降速（修改 ProcessingSpeedMultiplier）
- 解锁：配方树 / 成就触发 Unlock
- 生产链可视化（图结构生成）
- 批量操作 UI（多选 / 筛选）

---

如需：我可以继续输出具体重构文件模板（MachineProcessService.cs / 改造后的 ProductionTickService / 单元测试示例）。  
回复 “要：文件模板” 或 列出文件名即可。

祝 Day 2 顺利！