# UIForm MVC/MVVM 文章补充：R3 介绍 + 架构图 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `UIForm_MVC_MVVM_Zhihu.md` 的 MVVM 章节扩充 R3 简介，并新增三张 drawio 架构图（MVC、MVVM、CharacterPanel 案例）及对应占位符。

**Architecture:** 纯内容修改任务——1 篇 Markdown 文章 + 3 个新建 drawio XML 文件，无代码逻辑。文章编辑通过 Edit 工具精确替换；drawio 文件通过 Write 工具写入 `ToolkitInPorject/assets/`。

**Tech Stack:** Markdown, draw.io XML（mxGraph 格式），与已有 `uikit-class-diagram.drawio` / `mvvm-dataflow.drawio` 风格保持一致。

---

### Task 1：更新文章——R3 段落 + 三处占位符

**Files:**
- Modify: `ToolkitInPorject/UIForm_MVC_MVVM_Zhihu.md`

- [ ] **Step 1：替换 R3 一句话为段落（第 206-207 行）**

将：
```
这里需要用的Github上的R3仓库，R3 是一个基于 Reactive Programming（响应式编程）的 C# 框架，如果不想使用，也可以自己实现类似的效果。
```
替换为：
```
这里要用到 GitHub 上的 [R3](https://github.com/Cysharp/R3) 仓库（作者是 UniRx 的原作者 neuecc），可以看作 UniRx 的重写版——基于现代 .NET API，性能更好，接口更简洁。它提供了 `ReactiveProperty<T>`（带订阅机制的值容器）、`IReadOnlyReactiveProperty<T>`（只读版本，用于对外暴露）和 `CompositeDisposable`（统一管理订阅生命周期），这三个类在后面会频繁出现。安装方式：Unity Package Manager → Add package from git URL，填入仓库地址即可。不想引入外部依赖的话，也可以自己实现一个简化版的 `ReactiveProperty`，核心无非是一个带 setter 事件的泛型包装。
```

- [ ] **Step 2：在 MVC 章节末插入架构图占位符**

在以下文字之后、`---` 分隔线之前插入：
```
三层职责一目了然：Model 管数据，View 管显示，Controller 管协调。开篇的三个问题都解决了：数据独立可测，购买逻辑在 Model 里可复用，View 只做渲染。
```

插入内容：
```

> **[架构图：MVC 分层与数据流]** assets/mvc-architecture.drawio
```

- [ ] **Step 3：在 MVVM 章节 View 小节末插入 MVVM 架构图占位符**

在以下行之前插入（即现有 `mvvm-dataflow.drawio` 占位符之前）：
```
> **[流程图：MVVM 数据流向]** `assets/mvvm-dataflow.drawio`
```

插入内容：
```
> **[架构图：MVVM 分层与数据流]** assets/mvvm-architecture.drawio

```

- [ ] **Step 4：在"完整示例"章节末插入 CharacterPanel 案例图占位符**

在以下文字之后、`---` 分隔线之前插入：
```
战斗系统调 `TakeDamage`，完全不需要知道 `CharacterPanelForm` 的存在。HP 条的更新路径是：`model.Hp.Value` 变化 → `HpRatio` 和 `HpText` 的订阅触发 → Slider 和 Text 自动刷新。整个链路不经过任何 Controller。
```

插入内容：
```

> **[架构图：CharacterPanel 案例]** assets/characterpanel-case.drawio
```

- [ ] **Step 5：读回验证**

用 Read 工具读取第 200-215 行（确认 R3 段落），第 149-157 行（确认 MVC 占位符），第 335-348 行（确认 MVVM 占位符顺序），第 360-370 行（确认案例占位符）。

---

### Task 2：创建 `mvc-architecture.drawio`

**Files:**
- Create: `ToolkitInPorject/assets/mvc-architecture.drawio`

- [ ] **Step 1：写入文件**

```xml
<mxfile host="Electron" version="21.0.0">
  <diagram id="mvc-architecture" name="MVC 架构">
    <mxGraphModel dx="1200" dy="800" grid="0" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="0" pageScale="1" pageWidth="900" pageHeight="420" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />

        <!-- Title -->
        <mxCell id="title" value="MVC 架构" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=16;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="350" y="12" width="200" height="28" as="geometry" />
        </mxCell>

        <!-- Section 1 label -->
        <mxCell id="sec1" value="▌ 分层结构" style="text;html=1;strokeColor=none;fillColor=none;align=left;fontSize=12;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">
          <mxGeometry x="40" y="48" width="140" height="22" as="geometry" />
        </mxCell>

        <!-- Model (yellow) header -->
        <mxCell id="mh" value="Model" style="text;html=1;strokeColor=#d6b656;fillColor=#fff2cc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="40" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- Model body -->
        <mxCell id="mb" value="存数据 + 业务规则&#xa;纯 C# 类，可单元测试&#xa;&#xa;+ Hp, Gold, Level&#xa;+ TrySpend(cost) : bool&#xa;+ TakeDamage(damage) : void" style="text;html=0;strokeColor=#d6b656;fillColor=#fffde7;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="40" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- Controller (blue) header -->
        <mxCell id="ch" value="Controller" style="text;html=1;strokeColor=#6c8ebf;fillColor=#dae8fc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="340" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- Controller body -->
        <mxCell id="cb" value="持有 Model + View 引用&#xa;协调两者，绑定事件&#xa;&#xa;CharacterPanelForm&#xa;+ OnInit()  绑按钮&#xa;+ OnOpen()  触发首次刷新&#xa;+ OnBuyClicked()  手动刷新" style="text;html=0;strokeColor=#6c8ebf;fillColor=#e8f4fc;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="340" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- View (green) header -->
        <mxCell id="vh" value="View" style="text;html=1;strokeColor=#82b366;fillColor=#d5e8d4;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="640" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- View body -->
        <mxCell id="vb" value="只持有 UI 元素&#xa;只提供 Refresh 方法&#xa;&#xa;CharacterPanelView&#xa;+ _hpBar, _hpText, _goldText&#xa;+ Refresh(CharacterModel)" style="text;html=0;strokeColor=#82b366;fillColor=#eafaea;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="640" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- Controller → Model -->
        <mxCell id="ecm" value="持有引用 / 调用方法" style="endArrow=open;endSize=8;html=1;fontSize=10;fontStyle=2;strokeColor=#d6b656;fontColor=#7d6300;exitX=0;exitY=0.5;exitDx=0;exitDy=0;entryX=1;entryY=0.5;entryDx=0;entryDy=0;" edge="1" source="ch" target="mh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Controller → View -->
        <mxCell id="ecv" value="持有引用 / 调用 Refresh()" style="endArrow=open;endSize=8;html=1;fontSize=10;fontStyle=2;strokeColor=#82b366;fontColor=#2d7600;exitX=1;exitY=0.5;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;" edge="1" source="ch" target="vh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Divider -->
        <mxCell id="div" value="" style="line;html=1;strokeColor=#cccccc;fillColor=none;" vertex="1" parent="1">
          <mxGeometry x="40" y="224" width="820" height="10" as="geometry" />
        </mxCell>

        <!-- Section 2 label -->
        <mxCell id="sec2" value="▌ 数据流" style="text;html=1;strokeColor=none;fillColor=none;align=left;fontSize=12;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">
          <mxGeometry x="40" y="240" width="120" height="22" as="geometry" />
        </mxCell>

        <!-- Flow: 5 nodes at y=272 -->
        <mxCell id="f1" value="用户点击" style="ellipse;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="70" y="272" width="120" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f2" value="Controller&#xa;.OnBuyClicked()" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="228" y="272" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f3" value="Model&#xa;.TrySpend()" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="396" y="272" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f4" value="View&#xa;.Refresh(model)" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="564" y="272" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f5" value="UI 更新" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="732" y="272" width="100" height="44" as="geometry" />
        </mxCell>

        <!-- Flow arrows -->
        <mxCell id="ef12" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#555555;" edge="1" source="f1" target="f2" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef23" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#555555;" edge="1" source="f2" target="f3" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef34" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#555555;" edge="1" source="f3" target="f4" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef45" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#555555;" edge="1" source="f4" target="f5" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Note -->
        <mxCell id="note" value="Controller 在两次调用之间手动传话；数据变化不会自动反映到 View" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=11;fontColor=#888888;fontStyle=2;" vertex="1" parent="1">
          <mxGeometry x="120" y="326" width="660" height="20" as="geometry" />
        </mxCell>

      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
```

- [ ] **Step 2：读回验证**

用 Read 工具读取 `ToolkitInPorject/assets/mvc-architecture.drawio` 前 10 行，确认文件存在且 `<diagram id="mvc-architecture">` 标签正常。

---

### Task 3：创建 `mvvm-architecture.drawio`

**Files:**
- Create: `ToolkitInPorject/assets/mvvm-architecture.drawio`

- [ ] **Step 1：写入文件**

```xml
<mxfile host="Electron" version="21.0.0">
  <diagram id="mvvm-architecture" name="MVVM 架构">
    <mxGraphModel dx="1200" dy="800" grid="0" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="0" pageScale="1" pageWidth="900" pageHeight="420" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />

        <!-- Title -->
        <mxCell id="title" value="MVVM 架构" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=16;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="350" y="12" width="200" height="28" as="geometry" />
        </mxCell>

        <!-- Section 1 label -->
        <mxCell id="sec1" value="▌ 分层结构" style="text;html=1;strokeColor=none;fillColor=none;align=left;fontSize=12;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">
          <mxGeometry x="40" y="48" width="140" height="22" as="geometry" />
        </mxCell>

        <!-- Model (orange) header -->
        <mxCell id="mh" value="Model" style="text;html=1;strokeColor=#d6b656;fillColor=#ffe6cc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="40" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- Model body -->
        <mxCell id="mb" value="ReactiveProperty&lt;T&gt; 字段&#xa;业务规则方法&#xa;&#xa;+ Hp : ReactiveProperty&lt;int&gt;&#xa;+ Gold : ReactiveProperty&lt;int&gt;&#xa;+ TrySpend() / TakeDamage()" style="text;html=0;strokeColor=#d6b656;fillColor=#fff8f0;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="40" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- ViewModel (blue) header -->
        <mxCell id="vmh" value="ViewModel" style="text;html=1;strokeColor=#6c8ebf;fillColor=#dae8fc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="340" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- ViewModel body -->
        <mxCell id="vmb" value="持有 Model，不持有 View&#xa;暴露只读响应式属性&#xa;&#xa;+ HpRatio : IReadOnlyReactiveProperty&#xa;+ HpText  : IReadOnlyReactiveProperty&#xa;+ GoldText : IReadOnlyReactiveProperty&#xa;+ Buy(cost) : void" style="text;html=0;strokeColor=#6c8ebf;fillColor=#e8f4fc;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="340" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- View (green) header -->
        <mxCell id="vh" value="View" style="text;html=1;strokeColor=#82b366;fillColor=#d5e8d4;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="640" y="78" width="220" height="34" as="geometry" />
        </mxCell>
        <!-- View body -->
        <mxCell id="vb" value="订阅 ViewModel，不持有 Model&#xa;CompositeDisposable 管生命周期&#xa;&#xa;CharacterPanelForm&#xa;+ _hpBar, _hpText, _goldText&#xa;+ OnOpen()  Subscribe&#xa;+ OnClose()  Dispose" style="text;html=0;strokeColor=#82b366;fillColor=#eafaea;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="640" y="112" width="220" height="96" as="geometry" />
        </mxCell>

        <!-- ViewModel → Model (holds) -->
        <mxCell id="evmm" value="持有引用 / 调用方法" style="endArrow=open;endSize=8;html=1;fontSize=10;fontStyle=2;strokeColor=#d6b656;fontColor=#7d6300;exitX=0;exitY=0.5;exitDx=0;exitDy=0;entryX=1;entryY=0.5;entryDx=0;entryDy=0;" edge="1" source="vmh" target="mh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- View → ViewModel (subscribes, dashed) -->
        <mxCell id="evvm" value="Subscribe 订阅" style="dashed=1;endArrow=open;endSize=8;html=1;fontSize=10;fontStyle=2;strokeColor=#6c8ebf;fontColor=#23527c;exitX=0;exitY=0.5;exitDx=0;exitDy=0;entryX=1;entryY=0.5;entryDx=0;entryDy=0;" edge="1" source="vh" target="vmh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- No-arrow note -->
        <mxCell id="nonote" value="ViewModel 不持有 View 引用" style="text;html=1;strokeColor=#c00000;fillColor=#fff0f0;align=center;fontSize=10;fontStyle=2;fontColor=#c00000;" vertex="1" parent="1">
          <mxGeometry x="340" y="216" width="220" height="20" as="geometry" />
        </mxCell>

        <!-- Divider -->
        <mxCell id="div" value="" style="line;html=1;strokeColor=#cccccc;fillColor=none;" vertex="1" parent="1">
          <mxGeometry x="40" y="244" width="820" height="10" as="geometry" />
        </mxCell>

        <!-- Section 2 label -->
        <mxCell id="sec2" value="▌ 数据流" style="text;html=1;strokeColor=none;fillColor=none;align=left;fontSize=12;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">
          <mxGeometry x="40" y="260" width="120" height="22" as="geometry" />
        </mxCell>

        <!-- Flow nodes at y=290 -->
        <mxCell id="f1" value="用户点击" style="ellipse;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="70" y="290" width="120" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f2" value="ViewModel&#xa;.Buy()" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="228" y="290" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f3" value="Model.Gold&#xa;.Value--" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#ffe6cc;strokeColor=#d6b656;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="396" y="290" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f4" value="订阅链&#xa;自动触发" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="564" y="290" width="130" height="44" as="geometry" />
        </mxCell>
        <mxCell id="f5" value="UI 更新" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="732" y="290" width="100" height="44" as="geometry" />
        </mxCell>

        <!-- Flow arrows -->
        <mxCell id="ef12" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#c00000;" edge="1" source="f1" target="f2" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef23" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#c00000;" edge="1" source="f2" target="f3" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef34" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#0070c0;" edge="1" source="f3" target="f4" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="ef45" value="" style="endArrow=block;endFill=1;html=1;strokeColor=#0070c0;" edge="1" source="f4" target="f5" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Legend -->
        <mxCell id="leg1" value="" style="endArrow=block;endFill=1;strokeColor=#c00000;html=1;" edge="1" parent="1">
          <mxGeometry relative="1" as="geometry">
            <mxPoint x="120" y="360" as="sourcePoint" />
            <mxPoint x="170" y="360" as="targetPoint" />
          </mxGeometry>
        </mxCell>
        <mxCell id="leg1t" value="用户输入 / 方法调用" style="text;html=1;strokeColor=none;fillColor=none;fontSize=10;" vertex="1" parent="1">
          <mxGeometry x="176" y="350" width="150" height="20" as="geometry" />
        </mxCell>
        <mxCell id="leg2" value="" style="endArrow=block;endFill=1;strokeColor=#0070c0;html=1;" edge="1" parent="1">
          <mxGeometry relative="1" as="geometry">
            <mxPoint x="410" y="360" as="sourcePoint" />
            <mxPoint x="460" y="360" as="targetPoint" />
          </mxGeometry>
        </mxCell>
        <mxCell id="leg2t" value="数据变化 / 自动响应" style="text;html=1;strokeColor=none;fillColor=none;fontSize=10;" vertex="1" parent="1">
          <mxGeometry x="466" y="350" width="150" height="20" as="geometry" />
        </mxCell>

      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
```

- [ ] **Step 2：读回验证**

用 Read 工具读取 `ToolkitInPorject/assets/mvvm-architecture.drawio` 前 10 行，确认文件存在且 `<diagram id="mvvm-architecture">` 标签正常。

---

### Task 4：创建 `characterpanel-case.drawio`

**Files:**
- Create: `ToolkitInPorject/assets/characterpanel-case.drawio`

- [ ] **Step 1：写入文件**

```xml
<mxfile host="Electron" version="21.0.0">
  <diagram id="characterpanel-case" name="CharacterPanel 案例">
    <mxGraphModel dx="1200" dy="800" grid="0" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="0" pageScale="1" pageWidth="900" pageHeight="480" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />

        <!-- Title -->
        <mxCell id="title" value="CharacterPanel 案例架构" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=16;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="280" y="12" width="340" height="28" as="geometry" />
        </mxCell>

        <!-- Layer labels -->
        <mxCell id="lb1" value="Model 层" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=11;fontStyle=1;fontColor=#7d4e00;" vertex="1" parent="1">
          <mxGeometry x="40" y="48" width="200" height="20" as="geometry" />
        </mxCell>
        <mxCell id="lb2" value="ViewModel 层" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=11;fontStyle=1;fontColor=#23527c;" vertex="1" parent="1">
          <mxGeometry x="350" y="48" width="200" height="20" as="geometry" />
        </mxCell>
        <mxCell id="lb3" value="View 层" style="text;html=1;strokeColor=none;fillColor=none;align=center;fontSize=11;fontStyle=1;fontColor=#1a6c1a;" vertex="1" parent="1">
          <mxGeometry x="660" y="48" width="200" height="20" as="geometry" />
        </mxCell>

        <!-- CharacterModel box -->
        <mxCell id="mh" value="CharacterModel" style="text;html=1;strokeColor=#d6b656;fillColor=#ffe6cc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="40" y="78" width="200" height="34" as="geometry" />
        </mxCell>
        <mxCell id="mb" value="+ Hp : ReactiveProperty&lt;int&gt;&#xa;+ Gold : ReactiveProperty&lt;int&gt;&#xa;+ MaxHp : ReactiveProperty&lt;int&gt;&#xa;&#xa;+ TakeDamage(damage)&#xa;+ TrySpend(cost) : bool" style="text;html=0;strokeColor=#d6b656;fillColor=#fff8f0;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="40" y="112" width="200" height="100" as="geometry" />
        </mxCell>

        <!-- CharacterViewModel box -->
        <mxCell id="vmh" value="CharacterViewModel" style="text;html=1;strokeColor=#6c8ebf;fillColor=#dae8fc;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="350" y="78" width="200" height="34" as="geometry" />
        </mxCell>
        <mxCell id="vmb" value="+ HpRatio  : IReadOnlyReactiveProperty&#xa;+ HpText   : IReadOnlyReactiveProperty&#xa;+ GoldText : IReadOnlyReactiveProperty&#xa;&#xa;+ Buy(cost) : void" style="text;html=0;strokeColor=#6c8ebf;fillColor=#e8f4fc;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="350" y="112" width="200" height="100" as="geometry" />
        </mxCell>

        <!-- CharacterPanelForm box -->
        <mxCell id="vh" value="CharacterPanelForm" style="text;html=1;strokeColor=#82b366;fillColor=#d5e8d4;align=center;fontSize=13;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="660" y="78" width="200" height="34" as="geometry" />
        </mxCell>
        <mxCell id="vb" value="+ _hpBar   : Slider&#xa;+ _hpText  : Text&#xa;+ _goldText : Text&#xa;+ _disposables : CompositeDisposable&#xa;&#xa;+ OnOpen()  Subscribe&#xa;+ OnClose()  Dispose" style="text;html=0;strokeColor=#82b366;fillColor=#eafaea;align=left;verticalAlign=top;spacingLeft=8;spacingTop=5;fontSize=11;" vertex="1" parent="1">
          <mxGeometry x="660" y="112" width="200" height="100" as="geometry" />
        </mxCell>

        <!-- 战斗系统 (external) -->
        <mxCell id="battle" value="战斗系统" style="ellipse;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontSize=12;" vertex="1" parent="1">
          <mxGeometry x="60" y="290" width="130" height="50" as="geometry" />
        </mxCell>

        <!-- 用户 (external) -->
        <mxCell id="user" value="用户" style="ellipse;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontSize=12;" vertex="1" parent="1">
          <mxGeometry x="700" y="290" width="120" height="50" as="geometry" />
        </mxCell>

        <!-- 战斗系统 → CharacterModel -->
        <mxCell id="ebm" value="TakeDamage()" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#c00000;fontColor=#c00000;exitX=0.5;exitY=0;exitDx=0;exitDy=0;entryX=0.3;entryY=1;entryDx=0;entryDy=0;" edge="1" source="battle" target="mb" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- 用户 → CharacterPanelForm -->
        <mxCell id="euv" value="点击按钮" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#c00000;fontColor=#c00000;exitX=0.5;exitY=0;exitDx=0;exitDy=0;entryX=0.5;entryY=1;entryDx=0;entryDy=0;" edge="1" source="user" target="vb" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- CharacterPanelForm → ViewModel: Buy() -->
        <mxCell id="evvm" value="Buy(100)" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#c00000;fontColor=#c00000;exitX=0;exitY=0.3;exitDx=0;exitDy=0;entryX=1;entryY=0.3;entryDx=0;entryDy=0;" edge="1" source="vh" target="vmh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- ViewModel → Model: TrySpend() -->
        <mxCell id="evmm" value="TrySpend()" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#c00000;fontColor=#c00000;exitX=0;exitY=0.3;exitDx=0;exitDy=0;entryX=1;entryY=0.3;entryDx=0;entryDy=0;" edge="1" source="vmh" target="mh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Model → ViewModel: ReactiveProperty 触发 -->
        <mxCell id="emvm" value="ReactiveProperty 触发" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#0070c0;fontColor=#0070c0;exitX=1;exitY=0.7;exitDx=0;exitDy=0;entryX=0;entryY=0.7;entryDx=0;entryDy=0;" edge="1" source="mh" target="vmh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- ViewModel → View: Subscribe 自动刷新 -->
        <mxCell id="evmv" value="Subscribe 自动刷新" style="endArrow=block;endFill=1;html=1;fontSize=10;fontStyle=2;strokeColor=#0070c0;fontColor=#0070c0;exitX=1;exitY=0.7;exitDx=0;exitDy=0;entryX=0;entryY=0.7;entryDx=0;entryDy=0;" edge="1" source="vmh" target="vh" parent="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>

        <!-- Legend -->
        <mxCell id="leg1" value="" style="endArrow=block;endFill=1;strokeColor=#c00000;html=1;" edge="1" parent="1">
          <mxGeometry relative="1" as="geometry">
            <mxPoint x="220" y="416" as="sourcePoint" />
            <mxPoint x="270" y="416" as="targetPoint" />
          </mxGeometry>
        </mxCell>
        <mxCell id="leg1t" value="用户输入 / 方法调用" style="text;html=1;strokeColor=none;fillColor=none;fontSize=10;" vertex="1" parent="1">
          <mxGeometry x="276" y="406" width="150" height="20" as="geometry" />
        </mxCell>
        <mxCell id="leg2" value="" style="endArrow=block;endFill=1;strokeColor=#0070c0;html=1;" edge="1" parent="1">
          <mxGeometry relative="1" as="geometry">
            <mxPoint x="460" y="416" as="sourcePoint" />
            <mxPoint x="510" y="416" as="targetPoint" />
          </mxGeometry>
        </mxCell>
        <mxCell id="leg2t" value="数据变化 / 自动响应" style="text;html=1;strokeColor=none;fillColor=none;fontSize=10;" vertex="1" parent="1">
          <mxGeometry x="516" y="406" width="150" height="20" as="geometry" />
        </mxCell>

      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
```

- [ ] **Step 2：读回验证**

用 Read 工具读取 `ToolkitInPorject/assets/characterpanel-case.drawio` 前 10 行，确认文件存在且 `<diagram id="characterpanel-case">` 标签正常。

---

## 变更清单

| 操作 | 文件 |
|------|------|
| 修改 | `ToolkitInPorject/UIForm_MVC_MVVM_Zhihu.md` |
| 新增 | `ToolkitInPorject/assets/mvc-architecture.drawio` |
| 新增 | `ToolkitInPorject/assets/mvvm-architecture.drawio` |
| 新增 | `ToolkitInPorject/assets/characterpanel-case.drawio` |
