# PACK Online Material Station

这是一个仅用于**物料绑定工站**的 MES 扫码应用（Vue3 + Vite 前端，.NET8 后端）。

## 功能范围
- 扫描产品条码并查询工单
- 拉取工步并展示物料绑定信息
- 逐件扫码完成物料绑定校验
- 调用 MES `CompleteCheckInput` 完成后台校验
- 调用 MES `PushPackMessageToMes` 上报物料工步结果
- 本地后端提供日志落盘接口 `/saveLogs`

## 已移除能力
- 定扭工步流程
- 定扭矩阵与重试逻辑
- 控制器 TCP 通讯
- SignalR 实时定扭通道
- 相关前端组件与后端服务

## 目录
- `src/` 前端代码
- `backend/MesScanner.Backend/` 日志落盘后端
- `scripts/mock_mes_api.cjs` 物料绑定 mock 接口

## 运行
### 前端
```bash
npm install
npm run dev
```

### 后端
```bash
cd backend/MesScanner.Backend
dotnet run
```

默认日志接口：`http://127.0.0.1:5246/saveLogs`

## 构建
```bash
npm run build
```

```bash
cd backend/MesScanner.Backend
dotnet build
```
