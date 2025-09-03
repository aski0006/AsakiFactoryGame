# 项目 Mini GDD (初版)

版本: v0.1  
作者: 郑钦  
日期: 2025-09-03  
目标阅读时长: < 10 分钟  
文档目的: 约束“首个可玩竖切面 (Vertical Slice 1)” 的范围，统一术语与方向，避免过度设计。

---

## 1. 愿景 / Vision
做一个“俯视角 · 随机世界探索 + 渐进式工厂自动化 + 消耗品驱动的轻副本战斗 + 小规模 2~4 人合作”的循环游戏。玩家在主世界搭建兼顾效率与美感的生产链，通过副本产出稀有资源反哺工厂升级，形成长期目标与协同分工。

核心体验关键词：探索发现 / 规划与优化 / 渐进复杂度 / 明确反馈 / 合作有价值。

---

## 2. 目标用户 / Target Audience
- 喜欢《戴森球计划》《Satisfactory》《Factorio》但想要更轻量版本 + Dungeon 小战斗刺激。
- 独立游戏/生存类轻度玩家，愿意学习基础物流。
- 小群好友（2~4人）一起分工：采集 / 建造 / 配方规划 / 副本战斗。

---

## 3. 核心循环 (Core Loop)
1. 探索主世界 → 采集基础资源 (木/矿/石等)
2. 建造基础机器 → 加工为中级材料
3. 合成“副本凭证 / 钥匙”类消耗品
4. 进入副本 → 战斗 → 获得稀有掉落 / 新配方 / 科技核心
5. 回到主世界 → 解锁新建筑 / 扩产 / 自动化提升
6. 进入下一轮（更高效率 → 更高阶副本）

竖切面目标：上述循环在 5~10 分钟内跑通一个最小回合。

---

## 4. 游戏支柱 (Design Pillars)
1. 渐进复杂度：逐层解锁；不会一开始被大量系统淹没。
2. 数据可读 & 可扩展：所有配方/物品/建筑数据驱动。
3. 决策的可见性：机器产出、物流堵塞、产能瓶颈可直观看到。
4. 副本不是刷怪堆伤害，而是提供“驱动技术成长的稀缺资源”。
5. 合作加速体验，而不是必须强制组队。

---

## 5. VS1（Vertical Slice 1）范围
包含：
- 小地图 (≈64×64 Tiles / 3×3 Chunks)
- 采集 2 类自然节点（树 / 铜矿）
- 物品体系：5~8 个（木材、铜矿、木板、铜锭、钥匙碎片、钥匙、稀有水晶）
- 机器：熔炉 (冶炼)、装配台（合成钥匙）
- 1 条基础配方链：木材→木板；铜矿→铜锭；(木板 + 铜锭) → 钥匙碎片 → (若干碎片) → 钥匙
- 放置/移除建筑 + 手动交互（放入/取出）
- 传送带 & 简单取放臂（只需一对：从熔炉输出→箱子）
- 副本：单房间 + 1 种敌人 + 击杀掉落“稀有水晶”
- 战斗：玩家发射基础弹丸，敌人追踪/碰撞/受伤死亡
- 存档：玩家状态 / 资源节点采集状态 / 已放置建筑 / 机器内部进度 / 简单库存
- 简单 UI：背包、机器面板、制作面板、进入副本提示
- 单人模式（联机架构预留接口但不实现同步）

不包含：
- 科技树完整分支
- 电力/流体系统
- 复杂副本生成 / Boss / Buff
- 大地图 Streaming
- 多种敌人行为 / AI 层级
- 美术精修（占位美术即可）

---

## 6. 系统概述 (Systems Overview)

### 6.1 物品 / Items
- 定义结构：Id(int), Name, Category(Enum), MaxStack(int), Rarity(optional), IconRef
- Category：Resource / Intermediate / Key / RareDrop / Tool
- 数据来源：ScriptableObject → 运行时构建 Dictionary<int, ItemDef>
- 后期：可转 JSON + 生成器

### 6.2 配方 / Recipes
- 结构：Id, Inputs(List<ItemStack>), Outputs(List<ItemStack>), TimeSeconds, MachineType(限制机器)
- Example：CopperOre x1 -> CopperIngot x1 (Time=4s)
- 验证：机器拉取输入 → 计时 → 产出放入内部库存

### 6.3 库存 / Inventory
- Slot 数量 VS1：玩家 24，箱子 16，机器内部输入/输出分组
- 操作：Add / Remove / CanMerge / Split / Events
- UI：拖拽 / Shift 快速移动

### 6.4 世界 / World
- TileGrid 简化：纯地形 + 资源节点对象 (ResourceNode: type, hp, respawn? VS1 不刷新)
- Chunk：32×32 or 16×16；VS1 可一次性加载
- 采集逻辑：按节点类型 → 掉落对应 Item → 节点标记“已空”

### 6.5 建筑 / Building
- 结构：Id, Size(w,h), PlacedPosition, Rotation, MachineComponent / CustomBehaviour
- 占格校验：简易阻挡数组
- 交互：选中显示 Outline，E 打开面板

### 6.6 机器运行 / Production
- 逻辑 Tick：统一 GameTickService（每 0.5s or 0.2s）
- MachineState: ActiveRecipeId, Progress, InternalInventory
- Tick 流程：检查输入 → 扣材料 → 增加 Progress → 完成时写入输出槽

### 6.7 物流 / Logistics (VS1 最小)
- Conveyor：离散槽数组（如长度 N，每槽 1 物品 or 空）
- 取放臂 Inserter：周期扫描来源输出槽 → 若目标可接收则搬运
- 箱子：普通 Inventory
- Debug：显示被阻塞（未来）

### 6.8 副本 / Dungeon
- 入口：消耗 Key Item -> 切换 Scene (Additive)
- 场景结构：1 房间 + 敌人 2~4 只
- 完成条件：全部清空 → 掉落 Rare Crystal → 返回主世界（按钮或时间传送）
- 不保存副本内中间状态（只保留奖励）

### 6.9 敌人 / Enemy & 战斗
- 敌人：简单追玩家（距离 < 视野时）+ 接触或被子弹命中扣血
- Player：Move / Shoot（CD，直线弹）
- 生命：HealthComponent(HP, Max, Damage(amount))
- 掉落：固定 RareCrystal x1（VS1）

### 6.10 多人 / Multiplayer (预留)
- 暂不实现同步；保留接口：INetworkIdentity / EntityId
- 架构原则：生产、世界状态未来 Server Authoritative

### 6.11 存档 / Save System
- Sections：
    - PlayerSection（位置、背包）
    - WorldSection（资源节点状态：坐标 -> 枯竭标记）
    - BuildingSection（建筑列表、类型、位置、机器状态）
    - InventorySection（全局箱子/机器内部）
- Dirty 标记：修改即登记 → 保存
- 格式：JSON (UnityJsonSerializer) / 后期可压缩加密

### 6.12 UI
- HUD：血量 / 当前选中物品 / 提示
- InventoryPanel：网格
- MachinePanel：输入栏 / 输出栏 / 进度条 / 当前配方
- CraftPanel：可手动制作（钥匙碎片 / 钥匙）

### 6.13 Progression / 解锁
- VS1 极简：钥匙合成需要中级产物 → 副本掉落“稀有水晶”暂不用于其他链条，只作为“完成标志”
- VS2 起再加入科技树

---

## 7. 数据初稿 (示例)

物品表（草案）：
1. Wood (ID=1) Resource, Stack=100
2. CopperOre (2) Resource, Stack=100
3. WoodPlank (3) Intermediate, Stack=100
4. CopperIngot (4) Intermediate, Stack=100
5. KeyFragment (5) Key, Stack=50
6. DungeonKey (6) Key, Stack=10
7. RareCrystal (7) RareDrop, Stack=50

配方：
- Wood → WoodPlank (1:1, 2s, MachineType=Assembler or HandCraft? 决策：手搓 or 机器二选一。建议 VS1 设为机器产出增加必要性)
- CopperOre → CopperIngot (1:1, 4s, MachineType=Furnace)
- WoodPlank + CopperIngot → KeyFragment (2 + 1 -> 1, 6s, MachineType=Assembler)
- KeyFragment ×4 → DungeonKey (手工 or Assembler 8s)
  （可按实际节奏调）

机器：
- Furnace (冶炼)
- Assembler (装配台)

---

## 8. 技术结构 (目录期望)

Scripts/
Core/ (事件、时间、上下文、工具)
Data/ (定义 + 加载)
Items/
Inventory/
World/
Building/
Machines/
Logistics/
Dungeon/
Combat/
Save/
UI/
DebugTools/

---

## 9. 里程碑 & 验收标准

Milestone VS1 完成条件：
- [ ] 启动 → 采集 → 建造机器 → 运行配方 → 制作钥匙 → 进入副本 → 击杀 → 获得稀有水晶 → 返回
- [ ] 全程无致命错误 & 可二次进入
- [ ] Save/Load：退出重进恢复建筑/库存/世界节点状态
- [ ] 运行稳定：≤10 建筑 + 简单物流 60 FPS
- [ ] 交互平均不超过 3 次点击即可完成“采集→投料→取出”

KPI（内部衡量）：
- 首次循环耗时：目标 ≤10 分钟
- 玩家从原木到铜锭配方理解时间：≤1 分钟
- 机器阻塞原因可从 UI 或肉眼 5 秒内判断

---

## 10. 未来扩展（VS1 后再做）
- 电力系统（发电机 → 线路 → 供能限制）
- 科技树（解锁新机器/配方）
- 更大世界：多 Biome / 地图渐进扩展
- 高阶物流（分拣/过滤/巴士化/流体管线）
- 多敌人类型：远程 / 范围 / 精英
- 更复杂副本生成（随机房间 / 事件 / 计时挑战）
- 多人同步实现 + 分工 Buff

---

## 11. 风险与对策
| 风险 | 描述 | 预防 |
| ---- | ---- | ---- |
| 过度范围膨胀 | 想一次做完科技/电力/副本 | 文档锁定 VS1 范围，新增创意放入 backlog |
| 数据硬编码 | 物品写死脚本里 | 统一用配置文件 & Id |
| 性能提前优化 | 早期浪费时间 | VS1 不做 ECS/Job，先正确性 |
| Save 膨胀 | 全量写入 | Section + Dirty 标记 |
| 副本与主世界耦合 | 修改副本影响主世界 | 场景隔离 + 数据单向返回 |
| 逻辑与渲染混杂 | Update 中直接写状态 | 抽出 Tick 服务，渲染做插值 |

---

## 12. 术语表
- VS1：Vertical Slice 1
- Node：资源节点
- Machine：可运行配方建筑
- Inserter：取放装置
- IO：输入输出接口
- Section：存档分区
- Key / DungeonKey：进入副本消耗品
- RareCrystal：副本奖励占位物

---

## 13. 命名规范（建议）
- 脚本：PascalCase (ResourceNode, MachineState)
- 接口：I + 名 (IItemIO, ISaveSection)
- 配置资产：前缀 Item_, Recipe_, Machine_
- 事件：OnXxx / XxxEvent
- 资源文件夹：Assets/Data/Items, Assets/Data/Recipes（或生成后的）

---

## 14. 立即 TODO（落实文档）
1. 填写本文件作者/日期并 Commit。
2. 建 Data 目录 + ScriptableObject 定义：ItemDefinition / RecipeDefinition / MachineDefinition。
3. 编写 ItemDatabase、RecipeDatabase 加载器。
4. 实现 Inventory + 简易调试窗口（可生成物品、增删）。
5. 世界小地图 + 2 种 ResourceNode 可采集。
6. Furnace + Assembler 运行 + 进度条。
7. 保存/加载：玩家位置 + 节点状态 + 建筑列表 + 机器进度。
8. 单房间副本 + 敌人 + 掉落。
9. 整体验收一轮（录屏 & 笔记反馈）。

---

## 15. 附录：最小数据结构（伪代码）

```csharp
public class ItemDef { public int Id; public string Name; public ItemCategory Category; public int MaxStack; }

public class RecipeDef { public int Id; public ItemStack[] Inputs; public ItemStack[] Outputs; public float Time; public MachineType MachineType; }

public struct ItemStack { public int ItemId; public int Count; }

public class MachineState {
    public int InstanceId;
    public int? ActiveRecipeId;
    public float Progress;
    public Inventory Internal;
}

public class ResourceNodeState {
    public int NodeId;
    public Vector2Int Pos;
    public bool Depleted;
    public int DefId;
}
```

---

结束。本版本只聚焦 VS1，可在完成后派生 v0.2 增补科技与联机计划。  
（后续新增内容统一附加在“变更记录”章节）
