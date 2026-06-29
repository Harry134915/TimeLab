# Current Status

## 已完成

* 项目脚手架
* Core 模型
* Application 服务
* Infrastructure JSON 存储
* WPF UI
* Todo 创建、完成、删除
* 任务计划时长
* 任务关联计时
* 番茄钟开始、暂停、停止、清除
* 预设时长倒计时
* 专注 / 短休 / 长休模式
* 循环番茄模式
* Session Log 自动记录
* 今日统计
* 深色模式
* 系统托盘提醒
* 快捷键支持
* `TimeLab.Tests` 测试项目
* Application 服务和 JSON 存储的基础测试

---

## 当前状态

* 项目可编译
* MVP+ 功能可运行
* 分层结构基本符合当前架构规则
* 数据使用 JSON 文件保存在本地
* 当前没有引入数据库
* 已在用户同意后引入 xUnit 测试框架
* 第三方库或框架允许在必要时引入，但必须先征得用户同意

---

## 下一步计划

* 保持文档与当前实现同步
* 检查计时逻辑的正确性，尤其是暂停 / 继续后的 Session 时间记录
* 简化 `MainViewModel`，拆分过长的 UI 交互逻辑
* 减少 JSON 仓储中的重复读写代码
* 继续补充缺失的 Application / Infrastructure 测试场景
* 在代码结构稳定后，再考虑 AI Insight 原型
