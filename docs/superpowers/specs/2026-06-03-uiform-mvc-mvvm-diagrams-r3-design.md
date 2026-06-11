# UIForm MVC/MVVM 文章补充：R3 介绍 + 架构图设计

**日期：** 2026-06-03  
**目标文件：** `ToolkitInPorject/UIForm_MVC_MVVM_Zhihu.md`

---

## 目标

1. 扩充 MVVM 章节中的 R3 简介（从一句话扩展为段落）
2. 新增三张 drawio 架构图，并在文章正文对应位置插入占位符

---

## 一、R3 介绍段落

**位置：** 替换现有第 206-207 行（`### ReactiveProperty：赋值即通知` 代码块之前的一句话）

**内容：**
> 这里要用到 GitHub 上的 [R3](https://github.com/Cysharp/R3) 仓库（作者是 UniRx 的原作者 neuecc），可以看作 UniRx 的重写版——基于现代 .NET API，性能更好，接口更简洁。它提供了 `ReactiveProperty<T>`（带订阅机制的值容器）、`IReadOnlyReactiveProperty<T>`（只读版本，用于对外暴露）和 `CompositeDisposable`（统一管理订阅生命周期），这三个类在后面会频繁出现。安装方式：Unity Package Manager → Add package from git URL，填入仓库地址即可。不想引入外部依赖的话，也可以自己实现一个简化版的 `ReactiveProperty`，核心无非是一个带 setter 事件的泛型包装。

---

## 二、架构图设计

### 图 1：`mvc-architecture.drawio`

**插入位置：** MVC 章节末（"三层职责一目了然…" 段落之后，`---` 分隔线之前）  
**占位符：** `> **[架构图：MVC 分层与数据流]** assets/mvc-architecture.drawio`

**内容：**
- **上半部分（结构）：** 三个竖向方框 Model / Controller / View，标注各层职责；Controller 同时持有 Model 和 View 引用（箭头指向）
- **下半部分（数据流）：** `用户点击` → `Controller.OnBuyClicked()` → `Model.TrySpend()` → Controller 手动调 `View.Refresh(model)` → UI 更新

---

### 图 2：`mvvm-architecture.drawio`

**插入位置：** MVVM 章节末（View 小节之后，`---` 分隔线之前，现有 `mvvm-dataflow.drawio` 占位符之前）  
**占位符：** `> **[架构图：MVVM 分层与数据流]** assets/mvvm-architecture.drawio`

**内容：**
- **上半部分（结构）：** 三个竖向方框 Model / ViewModel / View
  - Model：`ReactiveProperty<T>` 字段
  - ViewModel：持有 Model，对外暴露 `IReadOnlyReactiveProperty`，提供操作方法；**不持有 View 引用**
  - View：订阅 ViewModel，用 `CompositeDisposable` 管理生命周期
- **下半部分（数据流）：** `用户点击` → `ViewModel.Buy()` → `Model.Gold.Value--` → 订阅链自动触发 → UI 更新

---

### 图 3：`characterpanel-case.drawio`

**插入位置：** "完整示例" 代码块之后，`---` 分隔线之前  
**占位符：** `> **[架构图：CharacterPanel 案例]** assets/characterpanel-case.drawio`

**内容（具体类名，单界面视角）：**
- 外部角色：`战斗系统`（调用 `TakeDamage`）、`用户`（点击按钮）
- `CharacterModel`：`Hp`、`Gold`（ReactiveProperty）、`TakeDamage()`、`TrySpend()`
- `CharacterViewModel`：`HpRatio`、`HpText`、`GoldText`（IReadOnlyReactiveProperty）、`Buy(cost)`
- `CharacterPanelForm`：`_hpBar`、`_hpText`、`_goldText`，`CompositeDisposable`

（现有 `mvvm-dataflow.drawio` 保留原位不动）

---

## 三、文件变更清单

| 操作 | 文件 |
|------|------|
| 修改 | `ToolkitInPorject/UIForm_MVC_MVVM_Zhihu.md` |
| 新增 | `ToolkitInPorject/assets/mvc-architecture.drawio` |
| 新增 | `ToolkitInPorject/assets/mvvm-architecture.drawio` |
| 新增 | `ToolkitInPorject/assets/characterpanel-case.drawio` |
