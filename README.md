# TimeLab

TimeLab 是一个简单的时间管理工具，包含：

* Todo 列表
* 番茄钟
* 专注记录（Session Log）

---

## 技术栈

* C#
* WPF
* MVVM
* JSON 本地存储

---

## 功能

### Todo

* 创建任务
* 完成任务
* 删除任务

### Timer

* 开始 / 暂停 / 停止

### Session Log

* 自动记录专注时间

---

## 运行方式

1. 打开 TimeLab.sln
2. 运行 TimeLab.App

---

## 项目结构

* TimeLab.App：UI 层
* TimeLab.Application：业务逻辑
* TimeLab.Core：领域模型
* TimeLab.Infrastructure：数据存储