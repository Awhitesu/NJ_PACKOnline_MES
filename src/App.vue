<script setup lang="ts">
import { ref, reactive, onMounted, nextTick } from 'vue'
import type { AppConfig, OrderInfo, RouteStep, TestResult, User } from './types/mes'
import {
  getOrderByProcess,
  getRouteList,
  completeCheckInput,
  pushPackMessageToMes,
  readOrderStatusSelectionFromFile,
  saveOrderStatusSelectionToFile
} from './services/mesApi'
import ConfigModal from './components/ConfigModal.vue'
import RouteTable from './components/RouteTable.vue'
import ApiDetail from './components/ApiDetail.vue'
import type { ApiRecord } from './components/ApiDetail.vue'
import MaterialScanner from './components/MaterialScanner.vue'
import LoginModal from './components/LoginModal.vue'

const CONFIG_KEY = 'mes_app_config_v3'
const DEFAULT_CONFIG: AppConfig = {
  orderApiUrl: '/mes-api/api/OrderInfo/GetOtherOrderInfoByProcess',
  routeApiUrl: '/mes-api/api/OrderInfo/GetTechRouteListByCode',
  technicsProcessCode: 'CTP_P1240',
  logSavePath: 'C:\\NJ_Material_Logs',
  adminUsername: 'admin',
  adminPassword: '123'
}

function loadConfig(): AppConfig {
  try {
    const raw = localStorage.getItem(CONFIG_KEY)
    if (raw) return { ...DEFAULT_CONFIG, ...JSON.parse(raw) }
  } catch {
    // ignore parse errors and use defaults
  }
  return { ...DEFAULT_CONFIG }
}

function getOrderCode(order: Partial<OrderInfo> | null | undefined): string {
  if (!order) return ''
  const keys = [
    'code',
    'Code',
    'orderCode',
    'order_Code',
    'order_code',
    'produceOrderCode',
    'produce_OrderCode',
    'produce_order_code',
    'orderNo',
    'order_No',
    'orderNO',
    'poCode',
    'sourceOrderCode'
  ]
  const data = order as any
  const val = keys.map((k) => data[k]).find((v) => v !== undefined && v !== null && String(v).trim() !== '')
  return val ? String(val).trim() : ''
}

function getOrderStatus(order: Partial<OrderInfo> | null | undefined): string {
  if (!order) return ''
  const data = order as any
  const statusVal =
    data.order_status ??
    data.orderStatus ??
    data.order_Status ??
    data.ORDER_STATUS ??
    data.status ??
    data.Status ??
    data.orderstatus
  if (statusVal === undefined || statusVal === null || String(statusVal).trim() === '') return ''
  const text = String(statusVal).trim()
  if (text === '2') return '下发中'
  return text
}

function getOrderWorkSeqNo(order: Partial<OrderInfo> | null | undefined): string {
  void order
  return (config.technicsProcessCode || '').trim()
}

function normalizeOrderList(res: any): OrderInfo[] {
  const candidates: OrderInfo[] = []
  const seen = new Set<string>()

  function addCandidate(item: any) {
    if (!item || typeof item !== 'object') return
    const code = getOrderCode(item)
    const status = getOrderStatus(item)
    if (!code && !status) return
    const key = `${code || 'NO_CODE'}|${status}`
    if (seen.has(key)) return
    seen.add(key)
    candidates.push(item as OrderInfo)
  }

  function walk(node: any) {
    if (!node) return
    if (Array.isArray(node)) {
      node.forEach((v) => walk(v))
      return
    }
    if (typeof node !== 'object') return

    addCandidate(node)
    Object.values(node).forEach((v) => {
      if (v && (Array.isArray(v) || typeof v === 'object')) walk(v)
    })
  }

  walk(res)
  return candidates
}

const config = reactive<AppConfig>(loadConfig())
const showConfig = ref(false)
const showLogin = ref(false)
const currentUser = ref<User | null>(null)
const onConfigSaved = () => localStorage.setItem(CONFIG_KEY, JSON.stringify(config))

const productCode = ref('')
const scanInputRef = ref<HTMLInputElement | null>(null)
const focusScan = () => nextTick(() => scanInputRef.value?.focus())

const orderInfo = ref<OrderInfo | null>(null)
const orderLoading = ref(false)
const orderError = ref('')
const routeSteps = ref<RouteStep[]>([])
const routeLoading = ref(false)
const routeError = ref('')

const testResult = ref<TestResult>('IDLE')
const resultMessage = ref('')
const logs = ref<{ time: string; level: 'info' | 'success' | 'warn' | 'error'; msg: string }[]>([])
const apiRecords = ref<ApiRecord[]>([])
const activeTab = ref<'route' | 'api' | 'log' | 'material'>('route')

const materialVerificationLoading = ref(false)
const materialVerificationSuccess = ref(false)
const verifiedMaterials = ref<Array<{ productCode: string; productCount: number }>>([])
const processStartTime = ref(new Date().toLocaleString())

const inProgressOrderCodes = ref<string[]>([])
const currentOrderStatus = ref<string>('')
const currentSelectedOrderCode = ref('')
const currentOrderWorkSeqNo = ref('')

const showOrderSelectModal = ref(false)
const selectingOrders = ref<OrderInfo[]>([])
const orderSelectionDraft = ref('')
const orderSelectionReason = ref('')
let orderSelectionResolver: ((code: string | null) => void) | null = null

function addLog(level: 'info' | 'success' | 'warn' | 'error', msg: string) {
  logs.value.unshift({ time: new Date().toLocaleTimeString(), level, msg })
  if (logs.value.length > 200) logs.value.pop()
}

function setCurrentOrderState(order: OrderInfo | null) {
  orderInfo.value = order
  currentSelectedOrderCode.value = getOrderCode(order)
  currentOrderStatus.value = getOrderStatus(order)
  currentOrderWorkSeqNo.value = getOrderWorkSeqNo(order)
}

function resetAll() {
  orderError.value = ''
  routeError.value = ''
  routeSteps.value = []
  orderInfo.value = null
  currentSelectedOrderCode.value = ''
  currentOrderStatus.value = ''
  currentOrderWorkSeqNo.value = ''
  testResult.value = 'IDLE'
  resultMessage.value = ''
  materialVerificationSuccess.value = false
  materialVerificationLoading.value = false
  verifiedMaterials.value = []
  processStartTime.value = new Date().toLocaleString()
}

function openOrderSelectionModal(orders: OrderInfo[], reason: string): Promise<string | null> {
  selectingOrders.value = orders
  orderSelectionReason.value = reason
  orderSelectionDraft.value = getOrderCode(orders[0])
  showOrderSelectModal.value = true

  return new Promise((resolve) => {
    orderSelectionResolver = resolve
  })
}

function closeOrderSelectionModal(selectedCode: string | null) {
  showOrderSelectModal.value = false
  const resolver = orderSelectionResolver
  orderSelectionResolver = null
  if (resolver) resolver(selectedCode)
}

async function fetchInProgressOrders(scene: '初始化' | '扫码前'): Promise<OrderInfo[]> {
  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: `获取首工单(${scene})`,
    url: config.orderApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: {
      produce_Type: 3,
      tenantID: 'FD'
    }
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await getOrderByProcess(config)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const ok = String(res?.code ?? '') === '200' || res?.success === true
    if (!ok) {
      const msg = res?.message || res?.msg || '获取工单失败'
      rec.status = 'error'
      throw new Error(msg)
    }

    const allOrders = normalizeOrderList(res)
    if (!allOrders.length) {
      rec.status = 'error'
      throw new Error('获取工单成功，但返回列表为空')
    }

    const releasingOrders = allOrders.filter((item) => getOrderStatus(item) === '下发中')
    inProgressOrderCodes.value = releasingOrders.map((item) => getOrderCode(item)).filter(Boolean)

    if (releasingOrders.length) {
      rec.status = 'success'
      addLog('info', `[工单状态] 当前下发中(order_status=下发中)工单数: ${releasingOrders.length}`)
    } else {
      rec.status = 'success'
      addLog('warn', `[工单状态] 获取工单成功，但没有 order_status=下发中 项 (共识别 ${allOrders.length} 条)`)
    }

    return releasingOrders
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    throw err
  }
}

async function ensureOrderSelection(scene: '初始化' | '扫码前'): Promise<boolean> {
  let releasingOrders: OrderInfo[] = []

  try {
    releasingOrders = await fetchInProgressOrders(scene)
  } catch (err: any) {
    orderError.value = `获取首工单失败: ${err?.message || String(err)}`
    addLog('error', `[工单状态] ${orderError.value}`)
    alert(orderError.value)
    return false
  }

  if (!releasingOrders.length) {
    orderError.value = '当前没有 order_status=下发中 的可选工单，流程停止。'
    alert(orderError.value)
    return false
  }

  let savedCode = ''
  let hasLocalFileRecord = false
  try {
    const saved = await readOrderStatusSelectionFromFile()
    savedCode = saved?.selectedCode?.trim() || ''
    hasLocalFileRecord = !!savedCode
  } catch (err: any) {
    const msg = `读取本地状态文件失败: ${err?.message || String(err)}`
    addLog('error', `[工单状态] ${msg}`)
    alert(`${msg}\n请先确认后端已启动。`)
    return false
  }

  if (hasLocalFileRecord && savedCode) {
    const matched = releasingOrders.find((item) => getOrderCode(item) === savedCode)
    if (matched) {
      setCurrentOrderState(matched)
      addLog('success', `[工单状态] 命中文件记录工单: ${savedCode}`)
      return true
    }
  }

  const reason = hasLocalFileRecord
    ? `文件中的上次工单 [${savedCode}] 不在当前 order_status=下发中 列表中，请重新选择并写入文件。`
    : '首次运行或本地无记录。请从 order_status=下发中 列表中选择当前工单，然后写入文件。'

  const selectedCode = await openOrderSelectionModal(releasingOrders, reason)
  if (!selectedCode) {
    addLog('warn', '[工单状态] 操作员取消了工单确认')
    alert('未确认当前工单，流程已停止。')
    return false
  }

  const selectedOrder = releasingOrders.find((item) => getOrderCode(item) === selectedCode)
  if (!selectedOrder) {
    addLog('error', '[工单状态] 选择的工单不在当前可选列表中')
    return false
  }

  try {
    await saveOrderStatusSelectionToFile({
      selectedCode,
      orderStatus: '下发中',
      updatedAt: new Date().toLocaleString()
    })
  } catch (err: any) {
    const msg = `保存状态文件失败: ${err?.message || String(err)}`
    addLog('error', `[工单状态] ${msg}`)
    alert(msg)
    return false
  }

  setCurrentOrderState(selectedOrder)
  addLog('success', `[工单状态] 已确认并写入文件: ${selectedCode}`)
  return true
}

async function initializeOrderStatusCheck() {
  addLog('info', '[初始化] 启动时先校验首工单状态...')
  const ok = await ensureOrderSelection('初始化')
  if (!ok) {
    resultMessage.value = '请先完成首工单确认后再扫码'
    return
  }
  resultMessage.value = '首工单状态校验完成，可以开始扫码。'
}

onMounted(async () => {
  focusScan()
  await initializeOrderStatusCheck()
})

async function handleScan() {
  const code = productCode.value.trim()
  if (!code || !config.technicsProcessCode) return

  resetAll()
  orderLoading.value = true
  activeTab.value = 'api'
  addLog('info', `[流程] 新条码 ${code}，先校验首工单状态`)

  try {
    const ok = await ensureOrderSelection('扫码前')
    if (!ok) {
      testResult.value = 'NG'
      resultMessage.value = '工单状态校验未通过，无法继续。'
      return
    }

    if (!orderInfo.value) {
      testResult.value = 'NG'
      resultMessage.value = '未能确定当前工单，请重新确认。'
      return
    }

    const routeNo = String((orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || '')
    const workSeqNo = (config.technicsProcessCode || '').trim()
    if (!workSeqNo) {
      testResult.value = 'NG'
      resultMessage.value = '配置中工序代码为空，无法下发工步。'
      addLog('error', '[工步] 配置中的工序代码为空，流程停止')
      return
    }
    addLog('success', `[工单] 当前确认工单: ${getOrderCode(orderInfo.value)}`)
    await fetchRouteList(routeNo, workSeqNo)
  } finally {
    orderLoading.value = false
  }
}

async function fetchRouteList(routeCode: string, workSeqNo: string) {
  if (!routeCode) {
    routeError.value = '工单缺少 routeNo，无法查询工步'
    addLog('error', `[工步] ${routeError.value}`)
    return
  }

  routeLoading.value = true
  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: '获取工步',
    url: config.routeApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: { routeCode, workSeqNo }
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await getRouteList(config, routeCode, workSeqNo)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const steps = (res.data as any)?.workSeqList || (Array.isArray(res.data) ? res.data : [])
    routeSteps.value = Array.isArray(steps) ? steps : []
    rec.status = 'success'
    addLog('success', `[工步] 成功加载 ${routeSteps.value.length} 条工序`)
    activeTab.value = 'material'
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    routeError.value = err?.message || '工步查询失败'
    addLog('error', `[工步] 查询失败: ${routeError.value}`)
  } finally {
    routeLoading.value = false
  }
}

function handleSingleMaterialScan(material: { productCode: string; productCount: number }) {
  const rec: ApiRecord = {
    title: '单物料扫码匹配',
    url: 'LOCAL_MATCH',
    status: 'success',
    time: new Date().toLocaleTimeString(),
    reqBody: material,
    resBody: { code: 200, message: '匹配成功' }
  }
  apiRecords.value.unshift(rec)
}

async function handleMaterialComplete(materials: { productCode: string; productCount: number }[]) {
  if (!orderInfo.value || materialVerificationLoading.value || materialVerificationSuccess.value) return

  materialVerificationLoading.value = true
  materialVerificationSuccess.value = false
  testResult.value = 'IDLE'
  resultMessage.value = '正在提交物料校验...'

  const reqData = {
    produceOrderCode: (orderInfo.value as any).orderCode || (orderInfo.value as any).order_Code || (orderInfo.value as any).code || '',
    routeNo: (orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || '',
    technicsProcessCode: config.technicsProcessCode,
    tenantID: 'FD',
    productMixCode: (orderInfo.value as any).productMixCode || (orderInfo.value as any).product_MixCode || null,
    productLine: '',
    materialList: materials
  }

  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: '全物料校验',
    url: '/mes-api/api/ProduceMessage/CompleteCheckInput',
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: reqData
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await completeCheckInput(config, reqData)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const success = !!(res && (res.code === 200 || res.code === '200' || res.success === true || res.msg === '操作成功'))
    if (!success) {
      const msg = res?.message || res?.msg || '未知错误'
      rec.status = 'error'
      testResult.value = 'NG'
      resultMessage.value = `物料校验未通过: ${msg}`
      addLog('error', `[物料校验] 失败: ${msg}`)
      return
    }

    rec.status = 'success'
    materialVerificationSuccess.value = true
    verifiedMaterials.value = materials
    testResult.value = 'OK'
    resultMessage.value = '物料校验通过，正在备份日志并报工...'
    addLog('success', '[物料校验] 全部通过，开始执行报工')

    await finalizeProcess()
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    testResult.value = 'NG'
    resultMessage.value = `物料校验请求异常: ${err?.message || String(err)}`
    addLog('error', `[物料校验] 请求异常: ${err?.message || String(err)}`)
  } finally {
    materialVerificationLoading.value = false
  }
}

async function finalizeProcess() {
  await saveAllLogsToLocal()
  await submitAllDataToMes()
}

async function submitAllDataToMes() {
  if (!orderInfo.value) return

  const t0 = Date.now()
  const nowDate = new Date().toLocaleDateString()
  const endTimeStr = new Date().toLocaleString()

  const payload = {
    produceOrderCode: (orderInfo.value as any).orderCode || (orderInfo.value as any).order_Code || (orderInfo.value as any).code || '',
    routeNo: (orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || '',
    technicsProcessCode: config.technicsProcessCode,
    technicsProcessName: '',
    technicsStepCode: 'STEP1',
    technicsStepName: '物料绑定',
    productCode: productCode.value,
    productCount: verifiedMaterials.value.length,
    productQuality: 0,
    produceDate: nowDate,
    startTime: processStartTime.value,
    endTime: endTimeStr,
    userName: currentUser.value?.username || 'admin',
    userAccount: currentUser.value?.username || 'admin',
    deviceCode: '',
    Remarks: '',
    ProduceInEntityList: verifiedMaterials.value.map((m) => ({
      productCode: m.productCode,
      ProductCount: m.productCount
    })),
    produceParamEntityList: [],
    ngEntityList: [],
    cellParamEntityList: [],
    otherParamEntityList: [],
    deviceName: ''
  }

  const finalPayload = [payload]
  const rec = reactive<ApiRecord>({
    title: 'MES 报工上传',
    url: '/mes-push/api/ProduceMessage/PushPackMessageToMes',
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: finalPayload
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await pushPackMessageToMes(config, finalPayload)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const success = !!(res && (res.code === 200 || res.success === true))
    if (!success) {
      const failMsg = res?.message || res?.msg || '服务端拒绝'
      rec.status = 'error'
      testResult.value = 'NG'
      resultMessage.value = `报工失败: ${failMsg}`
      addLog('error', `[报工] 失败: ${failMsg}`)
      return
    }

    rec.status = 'success'
    testResult.value = 'OK'
    resultMessage.value = '物料工站流程完成，报工已成功。'
    addLog('success', '[报工] 已成功推送 MES')
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    testResult.value = 'NG'
    resultMessage.value = `报工网络异常: ${err?.message || String(err)}`
    addLog('error', `[报工] 网络异常: ${err?.message || String(err)}`)
  }
}

async function saveAllLogsToLocal() {
  addLog('info', '[日志] 开始备份本地日志')

  const barcode = productCode.value.trim() || 'NoBarcode'
  const now = new Date()
  const timestamp =
    `${now.getFullYear()}` +
    `${(now.getMonth() + 1).toString().padStart(2, '0')}` +
    `${now.getDate().toString().padStart(2, '0')}_` +
    `${now.getHours().toString().padStart(2, '0')}` +
    `${now.getMinutes().toString().padStart(2, '0')}` +
    `${now.getSeconds().toString().padStart(2, '0')}`

  const fileName = `${barcode}_${timestamp}.txt`

  let content = '================================================\n'
  content += 'PACK_Material_MES 生产执行记录\n'
  content += `产品条码: ${barcode}\n`
  content += `当前工单: ${currentSelectedOrderCode.value || '未确认'}\n`
  content += `order_status: ${currentOrderStatus.value || '--'}\n`
  content += `保存时间: ${now.toLocaleString()}\n`
  content += '================================================\n\n'

  content += '【操作日志】\n'
  logs.value.slice().reverse().forEach((entry) => {
    content += `[${entry.time}] [${entry.level.toUpperCase()}] ${entry.msg}\n`
  })

  content += '\n【接口交互】\n'
  apiRecords.value.slice().reverse().forEach((record) => {
    content += '------------------------------------------------\n'
    content += `时间: ${record.time} | 状态: ${record.status.toUpperCase()} | 耗时: ${record.duration || 0}ms\n`
    content += `标题: ${record.title}\n`
    content += `请求: ${JSON.stringify(record.reqBody, null, 2)}\n`
    content += `响应: ${JSON.stringify(record.resBody, null, 2)}\n`
  })

  try {
    const response = await fetch('http://127.0.0.1:5246/saveLogs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        FileName: fileName,
        Content: content,
        Path: config.logSavePath
      })
    })

    if (!response.ok) {
      addLog('error', `[日志] 备份失败: HTTP ${response.status}`)
      return
    }

    const text = await response.text()
    try {
      const data = JSON.parse(text)
      addLog('success', `[日志] 已备份到: ${data.path}`)
    } catch {
      addLog('success', '[日志] 已发送备份请求')
    }
  } catch (err: any) {
    addLog('error', `[日志] 备份异常: ${err?.message || String(err)}`)
  }
}

async function resetResult() {
  const isFinished = testResult.value === 'OK' && resultMessage.value.includes('流程完成')
  const hasStarted = productCode.value.trim() !== ''

  if (hasStarted && !isFinished) {
    showLogin.value = true
    return
  }
  await executeReset()
}

async function handleAuthSuccess(user: User) {
  currentUser.value = user
  addLog('warn', `[授权] 管理员 ${user.username} 执行强制复位`)
  await executeReset()
  currentUser.value = null
}

async function executeReset() {
  if (productCode.value) {
    saveAllLogsToLocal().catch(() => {
      addLog('error', '[日志] 自动备份失败')
    })
  }

  productCode.value = ''
  routeSteps.value = []
  materialVerificationSuccess.value = false
  materialVerificationLoading.value = false
  verifiedMaterials.value = []
  testResult.value = 'IDLE'
  resultMessage.value = ''
  activeTab.value = 'route'

  addLog('info', '----------------------------------------')
  addLog('info', '[系统] 已复位，请扫描下一工单')
  focusScan()
}
</script>

<template>
  <div class="app-root">
    <header class="app-header">
      <div class="header-left">
        <div class="brand-icon">MES</div>
        <div class="brand-text">
          <span class="brand-title">工序扫码系统</span>
          <span class="brand-sub">Material Binding Station</span>
        </div>
      </div>
      <div class="header-center">
        <span class="process-badge">
          <span class="label">当前工序:</span>
          <span class="value">{{ config.technicsProcessCode || '未配置' }}</span>
        </span>
        <span class="order-status-badge" :class="currentOrderStatus === '下发中' ? 'ok' : 'warn'">
          <span class="label">order_status:</span>
          <span class="value">{{ currentOrderStatus || '--' }}</span>
        </span>
      </div>
      <div class="header-right">
        <button class="icon-btn" title="系统配置" @click="showConfig = true">配置</button>
      </div>
    </header>

    <main class="app-main">
      <section class="left-panel">
        <div class="card scan-card">
          <div class="card-title">1. 扫描产品条码</div>
          <div class="scan-input-wrap" :class="{ scanning: orderLoading }">
            <input
              ref="scanInputRef"
              v-model="productCode"
              type="text"
              placeholder="请扫描或输入产品条码"
              class="scan-input"
              :disabled="orderLoading || routeLoading"
              @keydown.enter="handleScan"
            />
            <button class="scan-btn" :disabled="orderLoading || !productCode.trim()" @click="handleScan">
              {{ orderLoading ? '校验中...' : '查询' }}
            </button>
          </div>
          <div class="scan-hint">每次扫码前都会先获取首工单并校验 order_status=下发中 列表</div>
        </div>

        <div class="card info-card">
          <div class="card-title">2. 工单信息</div>
          <div v-if="orderError" class="error-box">{{ orderError }}</div>
          <div v-else-if="orderInfo" class="info-grid">
            <div class="info-item">
              <span class="info-label">当前工单</span>
              <span class="info-value highlight">{{ currentSelectedOrderCode || '--' }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">order_status</span>
              <span class="info-value mono">{{ currentOrderStatus || '--' }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">工艺路线</span>
              <span class="info-value mono">{{ (orderInfo as any).route_No || (orderInfo as any).routeNo || '--' }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">workSeqNo</span>
              <span class="info-value mono">{{ currentOrderWorkSeqNo || '--' }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">下发中列表</span>
              <span class="info-value mono list" :title="inProgressOrderCodes.join('\n')">
                {{ inProgressOrderCodes.length ? inProgressOrderCodes.join(', ') : '--' }}
              </span>
            </div>
          </div>
          <div v-else class="empty-hint">等待首工单确认...</div>
        </div>

        <div class="card result-card">
          <div class="card-title">3. 流程状态</div>
          <div class="result-display" :class="testResult.toLowerCase()">
            <span class="result-text">{{ testResult === 'IDLE' ? '待执行' : testResult }}</span>
            <span v-if="resultMessage" class="result-msg">{{ resultMessage }}</span>
          </div>
          <button v-if="productCode.trim()" class="btn-reset" @click="resetResult">复位状态 / 下一个</button>
        </div>
      </section>

      <section class="right-panel">
        <div class="tab-bar">
          <button class="tab-btn" :class="{ active: activeTab === 'route' }" @click="activeTab = 'route'">
            工步列表
            <span v-if="routeSteps.length" class="tab-count">{{ routeSteps.length }}</span>
          </button>
          <button class="tab-btn" :class="{ active: activeTab === 'material' }" @click="activeTab = 'material'">物料校验</button>
          <button class="tab-btn" :class="{ active: activeTab === 'api' }" @click="activeTab = 'api'">
            接口交互
            <span v-if="apiRecords.length" class="tab-count">{{ apiRecords.length }}</span>
          </button>
          <button class="tab-btn" :class="{ active: activeTab === 'log' }" @click="activeTab = 'log'">
            操作日志
            <span v-if="logs.length" class="tab-count">{{ logs.length }}</span>
          </button>
        </div>

        <div class="tab-content">
          <div v-show="activeTab === 'route'" class="tab-pane">
            <div v-if="routeError" class="error-box">{{ routeError }}</div>
            <RouteTable :steps="routeSteps" :loading="routeLoading" />
          </div>

          <div v-show="activeTab === 'material'" class="tab-pane flex-column">
            <div v-if="materialVerificationLoading" class="status-banner loading-mini">正在提交全物料校验，请稍候...</div>
            <div v-if="materialVerificationSuccess" class="status-banner success-mini">物料校验通过，正在执行报工...</div>
            <div v-if="testResult === 'NG'" class="status-banner fail-mini">校验或报工失败: {{ resultMessage }}</div>
            <MaterialScanner
              :steps="routeSteps"
              @log="addLog"
              @single-complete="handleSingleMaterialScan"
              @complete="handleMaterialComplete"
            />
          </div>

          <div v-show="activeTab === 'api'" class="tab-pane">
            <ApiDetail :records="apiRecords" />
          </div>

          <div v-show="activeTab === 'log'" class="tab-pane log-pane">
            <div class="log-scroll">
              <div v-for="(entry, i) in logs" :key="i" class="log-entry" :class="entry.level">
                <span class="log-time">{{ entry.time }}</span>
                <span class="log-msg">{{ entry.msg }}</span>
              </div>
              <div v-if="!logs.length" class="log-empty">暂无日志</div>
            </div>
          </div>
        </div>
      </section>
    </main>

    <ConfigModal v-model="config" v-model:visible="showConfig" @save="onConfigSaved" />
    <LoginModal
      v-model:visible="showLogin"
      :admin-user="config.adminUsername"
      :admin-pass="config.adminPassword"
      @auth-success="handleAuthSuccess"
    />

    <div v-if="showOrderSelectModal" class="order-select-overlay">
      <div class="order-select-panel">
        <div class="order-select-title">请确认当前工单</div>
        <div class="order-select-sub">{{ orderSelectionReason }}</div>

        <div class="order-select-body">
          <label>order_status=下发中 可选工单</label>
          <select v-model="orderSelectionDraft" class="order-select-input">
            <option v-for="item in selectingOrders" :key="getOrderCode(item)" :value="getOrderCode(item)">
              {{ getOrderCode(item) }}
            </option>
          </select>

          <div class="order-list-preview">
            <div v-for="item in selectingOrders" :key="getOrderCode(item)" class="order-list-item">
              {{ getOrderCode(item) }}
            </div>
          </div>
        </div>

        <div class="order-select-actions">
          <button class="btn-cancel" @click="closeOrderSelectionModal(null)">取消</button>
          <button class="btn-confirm" :disabled="!orderSelectionDraft" @click="closeOrderSelectionModal(orderSelectionDraft)">
            确认工单
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.app-root {
  display: flex;
  flex-direction: column;
  height: 100vh;
  width: 100vw;
  background: #0a0e1a;
  color: #c8d6e5;
  font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif;
  overflow: hidden;
}

.app-header {
  display: flex;
  align-items: center;
  padding: 0 20px;
  height: 52px;
  background: linear-gradient(135deg, #0d1b2a 0%, #112240 100%);
  border-bottom: 1px solid rgba(100, 181, 246, 0.2);
  gap: 16px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.brand-icon {
  width: 34px;
  height: 34px;
  background: linear-gradient(135deg, #1565c0, #0d47a1);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  font-weight: 800;
  color: #e3f2fd;
}

.brand-text {
  display: flex;
  flex-direction: column;
}

.brand-title {
  font-size: 15px;
  font-weight: 700;
  color: #e3f2fd;
}

.brand-sub {
  font-size: 10px;
  color: #546e7a;
}

.header-center {
  flex: 1;
  display: flex;
  justify-content: center;
  gap: 8px;
}

.process-badge,
.order-status-badge {
  background: rgba(21, 101, 192, 0.2);
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 20px;
  padding: 4px 16px;
  font-size: 12px;
  display: flex;
  gap: 6px;
}

.order-status-badge.ok {
  border-color: rgba(0, 230, 118, 0.4);
}

.order-status-badge.warn {
  border-color: rgba(255, 171, 64, 0.4);
}

.process-badge .label,
.order-status-badge .label {
  color: #78909c;
}

.process-badge .value,
.order-status-badge .value {
  color: #42a5f5;
  font-weight: 600;
}

.icon-btn {
  background: rgba(21, 101, 192, 0.2);
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  color: #90caf9;
  padding: 5px 14px;
  font-size: 12px;
  cursor: pointer;
}

.app-main {
  display: flex;
  gap: 12px;
  padding: 12px;
  flex: 1;
  overflow: hidden;
}

.left-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 360px;
  flex-shrink: 0;
  overflow-y: auto;
}

.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: #131929;
  border: 1px solid rgba(100, 181, 246, 0.12);
  border-radius: 10px;
}

.card {
  background: #131929;
  border: 1px solid rgba(100, 181, 246, 0.15);
  border-radius: 10px;
  padding: 12px;
}

.card-title {
  font-size: 13px;
  font-weight: 700;
  color: #90caf9;
  margin-bottom: 10px;
}

.scan-input-wrap {
  display: flex;
  gap: 8px;
}

.scan-input {
  flex: 1;
  background: #0d1117;
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  color: #e0e6ed;
  padding: 10px;
}

.scan-btn {
  background: #1976d2;
  border: none;
  color: #fff;
  border-radius: 6px;
  padding: 0 16px;
  cursor: pointer;
}

.scan-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.scan-hint {
  margin-top: 8px;
  color: #78909c;
  font-size: 11px;
}

.error-box {
  background: rgba(244, 67, 54, 0.12);
  border: 1px solid rgba(244, 67, 54, 0.24);
  color: #ef9a9a;
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 12px;
}

.info-grid {
  display: grid;
  gap: 8px;
}

.info-item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 12px;
}

.info-label {
  color: #78909c;
  min-width: 90px;
}

.info-value {
  color: #e0e6ed;
  text-align: right;
}

.info-value.highlight {
  color: #80cbc4;
  font-weight: 700;
}

.mono {
  font-family: Consolas, monospace;
}

.info-value.list {
  max-width: 190px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-hint {
  color: #546e7a;
  font-size: 12px;
}

.result-display {
  border: 1px solid rgba(100, 181, 246, 0.12);
  border-radius: 6px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.result-display.idle {
  border-color: rgba(120, 144, 156, 0.4);
}

.result-display.ok {
  border-color: rgba(0, 230, 118, 0.45);
}

.result-display.ng {
  border-color: rgba(244, 67, 54, 0.45);
}

.result-text {
  font-size: 14px;
  font-weight: 700;
}

.result-msg {
  font-size: 12px;
  color: #b0bec5;
}

.btn-reset {
  margin-top: 10px;
  width: 100%;
  background: transparent;
  border: 1px solid rgba(100, 181, 246, 0.2);
  color: #90caf9;
  padding: 8px;
  border-radius: 6px;
  cursor: pointer;
}

.tab-bar {
  display: flex;
  gap: 2px;
  padding: 8px 10px 0;
  border-bottom: 1px solid rgba(100, 181, 246, 0.1);
}

.tab-btn {
  background: transparent;
  border: none;
  color: #78909c;
  font-size: 12px;
  padding: 8px 12px;
  border-radius: 6px 6px 0 0;
  cursor: pointer;
}

.tab-btn.active {
  background: rgba(21, 101, 192, 0.2);
  color: #e3f2fd;
}

.tab-count {
  margin-left: 6px;
  background: rgba(66, 165, 245, 0.2);
  color: #90caf9;
  border-radius: 10px;
  padding: 0 6px;
}

.tab-content {
  flex: 1;
  overflow: hidden;
}

.tab-pane {
  height: 100%;
  overflow: hidden;
}

.flex-column {
  display: flex;
  flex-direction: column;
}

.status-banner {
  margin: 10px;
  padding: 8px 10px;
  border-radius: 6px;
  font-size: 12px;
}

.loading-mini {
  background: rgba(255, 171, 64, 0.12);
  color: #ffcc80;
}

.success-mini {
  background: rgba(0, 230, 118, 0.1);
  color: #a5d6a7;
}

.fail-mini {
  background: rgba(244, 67, 54, 0.12);
  color: #ef9a9a;
}

.log-pane {
  padding: 10px;
}

.log-scroll {
  height: 100%;
  overflow-y: auto;
  border: 1px solid rgba(100, 181, 246, 0.1);
  border-radius: 8px;
  padding: 10px;
  background: #0d1117;
}

.log-entry {
  display: flex;
  gap: 10px;
  font-size: 12px;
  padding: 4px 0;
  border-bottom: 1px dashed rgba(100, 181, 246, 0.1);
}

.log-entry:last-child {
  border-bottom: none;
}

.log-time {
  color: #78909c;
  min-width: 80px;
}

.log-msg {
  flex: 1;
}

.log-entry.success .log-msg {
  color: #a5d6a7;
}

.log-entry.error .log-msg {
  color: #ef9a9a;
}

.log-entry.warn .log-msg {
  color: #ffcc80;
}

.log-empty {
  color: #546e7a;
  text-align: center;
  padding: 20px;
}

.order-select-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.65);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1300;
}

.order-select-panel {
  width: 520px;
  max-width: 92vw;
  background: #1a1f2e;
  border: 1px solid rgba(100, 181, 246, 0.28);
  border-radius: 10px;
  padding: 18px;
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.45);
}

.order-select-title {
  font-size: 17px;
  color: #e3f2fd;
  font-weight: 700;
}

.order-select-sub {
  margin-top: 6px;
  font-size: 12px;
  color: #ffcc80;
}

.order-select-body {
  margin-top: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.order-select-body label {
  font-size: 12px;
  color: #90caf9;
}

.order-select-input {
  width: 100%;
  background: #0d1117;
  border: 1px solid rgba(100, 181, 246, 0.25);
  border-radius: 6px;
  color: #e0e6ed;
  padding: 8px 10px;
}

.order-list-preview {
  max-height: 140px;
  overflow: auto;
  border: 1px solid rgba(100, 181, 246, 0.18);
  border-radius: 6px;
  background: #0d1117;
  padding: 6px;
}

.order-list-item {
  font-family: Consolas, monospace;
  font-size: 12px;
  color: #80cbc4;
  padding: 5px 6px;
  border-bottom: 1px dashed rgba(100, 181, 246, 0.1);
}

.order-list-item:last-child {
  border-bottom: none;
}

.order-select-actions {
  margin-top: 14px;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.btn-cancel,
.btn-confirm {
  min-width: 84px;
  padding: 8px 14px;
  border-radius: 6px;
  border: none;
  cursor: pointer;
}

.btn-cancel {
  background: #37474f;
  color: #cfd8dc;
}

.btn-confirm {
  background: #1976d2;
  color: white;
}

.btn-confirm:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
