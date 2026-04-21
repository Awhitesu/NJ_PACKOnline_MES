<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted, nextTick, computed } from 'vue'
import type { AppConfig, ModulePackCodeCreateRequest, OrderInfo, PrintLabelItem, RouteStep, TestResult, User } from './types/mes'
import {
  getOrderByProcess,
  getRouteList,
  singleCheckInput,
  completeCheckInput,
  pushPackMessageToMes,
  createModulePackCode,
  printLabelsByBarTender,
  pullBarcodeScanner,
  startBarcodeScanner,
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
  orderApiUrl: '/mes-api/api/OrderInfo/GetSourceOrderInfoByProcess',
  routeApiUrl: '/mes-api/api/OrderInfo/GetTechRouteListByCode',
  singleMaterialApiUrl: '/mes-api/api/ProduceMessage/SingleCheckInput',
  fullMaterialApiUrl: '/mes-api/api/ProduceMessage/CompleteCheckInput',
  codeCreateApiUrl: 'http://172.25.57.144:8034/api/CodeCreate/ModulePackCodeCreate',
  mesPushApiUrl: '/mes-push/api/ProduceMessage/PushPackMessageToMes',
  barTenderExePath: 'C:\\Program Files\\Seagull\\BarTender Suite\\bartend.exe',
  barTenderTemplatePath: 'C:\\BarTender\\Templates\\pack_label.btw',
  barTenderDatabasePath: 'C:\\BarTender\\Data\\pack_labels.csv',
  scannerIp: '172.25.57.144',
  scannerPort: 3000,
  barcodeMatchRegex: '^[0-9A-Za-z]{30}$',
  technicsProcessCode: 'CTP_P1240',
  technicsProcessName: '',
  userName: 'admin',
  userAccount: 'admin',
  deviceCode: '',
  deviceName: '',
  logSavePath: 'C:\\NJ_Material_Logs',
  adminUsername: 'admin',
  adminPassword: '123'
}

function loadConfig(): AppConfig {
  try {
    const raw = localStorage.getItem(CONFIG_KEY)
    if (raw) {
      const merged = { ...DEFAULT_CONFIG, ...JSON.parse(raw) } as AppConfig
      if (merged.orderApiUrl === '/mes-api/api/OrderInfo/GetOtherOrderInfoByProcess') {
        merged.orderApiUrl = '/mes-api/api/OrderInfo/GetSourceOrderInfoByProcess'
      }
      return merged
    }
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

function getOrderTenantId(order: Partial<OrderInfo> | null | undefined): string {
  return getOrderField(order, ['tenantID', 'tenantId', 'tenant_Id', 'lineCode', 'line_Code']) || 'FD'
}

function getOrderField(order: Partial<OrderInfo> | null | undefined, keys: string[]): string {
  if (!order) return ''
  const data = order as any
  for (const key of keys) {
    const val = data[key]
    if (val !== undefined && val !== null && String(val).trim() !== '') return String(val).trim()
  }
  return ''
}

function formatDateYYYYMMDD(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function validateProductBarcode(code: string): { ok: boolean; message?: string } {
  const rawPattern = (config.barcodeMatchRegex || '').trim()
  if (!rawPattern) return { ok: true }

  try {
    const regex = new RegExp(rawPattern)
    if (regex.test(code)) return { ok: true }
    return { ok: false, message: `产品条码不匹配规则: ${code} (正则不匹配)` }
  } catch (err: any) {
    return { ok: false, message: `产品条码规则无效: ${err?.message || String(err)}` }
  }
}

async function requestCreatePackCodes(
  cplx: 'S' | 'P',
  cplxname: string,
  xmh: string,
  ggdm: string,
  tzdm: string
): Promise<string[]> {
  const payload: ModulePackCodeCreateRequest = {
    csname: '合肥',
    bmtime: formatDateYYYYMMDD(new Date()),
    xmh,
    cplx,
    cplxname,
    ggdm,
    dysl: '1',
    dcbsl: '1',
    useR_NO: 'device001',
    useR_NAME: '设备001',
    dclx: '磷酸铁锂电池',
    scgc: '南京国轩公司',
    sccx: '三期PACK三线',
    tzdm,
    mzjx: '0'
  }

  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: `获取条码(${cplx})`,
    url: config.codeCreateApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: payload
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await createModulePackCode(config, payload)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const ok = String(res?.code ?? '') === '200' || res?.success === true
    const codes = Array.isArray(res?.data) ? res.data.map((v) => String(v).trim()).filter(Boolean) : []
    if (!ok || codes.length === 0) {
      const msg = res?.message || res?.msg || '条码请求失败'
      rec.status = 'error'
      throw new Error(codes.length === 0 ? `${msg}（返回data为空）` : msg)
    }

    rec.status = 'success'
    addLog('success', `[条码生成] ${cplxname}获取成功: ${codes.join(', ')}`)
    return codes
  } catch (err: any) {
    rec.status = 'error'
    if (!rec.resBody) rec.resBody = err?.message || String(err)
    throw err
  }
}

async function buildProduceInListByCodeCreate(): Promise<Array<{ productCode: string; productCount: number }> | null> {
  createdCodes.value = { s: [], p: [], updatedAt: '' }
  const xmh = getOrderField(orderInfo.value, ['xmh', 'projectCode', 'project_code', 'projectNo', 'project_No', 'projectNO'])
  const ggdm = getOrderField(orderInfo.value, ['ggdm', 'specsCode', 'specCode', 'productSpecCode', 'product_SpecCode', 'packSpecCode'])
  const tzdm = getOrderField(orderInfo.value, ['productProperty', 'product_Property', 'productproperty', 'tzdm']) || '8'

  if (!xmh || !ggdm) {
    const missing = [!xmh ? 'xmh(项目号)' : '', !ggdm ? 'ggdm(规格代码)' : ''].filter(Boolean).join('、')
    testResult.value = 'NG'
    resultMessage.value = `条码获取前置字段缺失: ${missing}`
    addLog('error', `[条码生成] 工单字段缺失，无法获取条码: ${missing}`)
    setGlobalStatus(`条码获取失败: ${missing}`, 'error')
    return null
  }

  resultMessage.value = '物料校验通过，正在获取S/P条码...'
  addLog('info', `[条码生成] 开始获取条码，xmh=${xmh}，ggdm=${ggdm}，tzdm=${tzdm}`)
  setGlobalStatus('正在获取条码...', 'info')

  try {
    const sCodes = await requestCreatePackCodes('S', '电池系统', xmh, ggdm, tzdm)
    const pCodes = await requestCreatePackCodes('P', '电池包', xmh, ggdm, tzdm)
    createdCodes.value = { s: sCodes, p: pCodes, updatedAt: new Date().toLocaleString() }
    addLog('info', `[条码生成] 汇总: S(${sCodes.join(', ')}) | P(${pCodes.join(', ')})`)
    setGlobalStatus('条码获取成功', 'success')

    const mesProductCode = productCode.value.trim()
    const mesCodes = [...pCodes]
    if (mesProductCode) mesCodes.push(mesProductCode)

    if (!mesCodes.length) {
      testResult.value = 'NG'
      resultMessage.value = '条码接口未返回可用条码'
      addLog('error', '[条码生成] 两次接口均未返回data条码')
      setGlobalStatus('条码获取失败', 'error')
      return null
    }
    addLog('info', `[报工] ProduceInEntityList 使用 P码+产品条码: ${mesCodes.join(', ')}`)
    return mesCodes.map((code) => ({ productCode: code, productCount: 1 }))
  } catch (err: any) {
    testResult.value = 'NG'
    resultMessage.value = `条码获取失败: ${err?.message || String(err)}`
    addLog('error', `[条码生成] 失败: ${err?.message || String(err)}`)
    setGlobalStatus(`条码获取失败: ${err?.message || String(err)}`, 'error')
    return null
  }
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
const onConfigSaved = async () => {
  localStorage.setItem(CONFIG_KEY, JSON.stringify(config))
  await restartBarcodeScanner()
}

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
const activeTab = ref<'route' | 'material' | 'code' | 'api' | 'log'>('route')

const materialVerificationLoading = ref(false)
const materialVerificationSuccess = ref(false)
const verifiedMaterials = ref<Array<{ productCode: string; productCount: number }>>([])
const createdCodes = ref<{ s: string[]; p: string[]; updatedAt: string }>({ s: [], p: [], updatedAt: '' })
const processStartTime = ref(new Date().toLocaleString())
const scannerAlertMessage = ref('')
const materialTaskCount = computed(() => {
  let total = 0
  routeSteps.value.forEach((seq: any) => {
    const wsList = (seq?.workStepList as any[]) || []
    wsList.forEach((ws: any) => {
      const matList = (ws?.workStepMaterialList as any[]) || []
      matList.forEach((mat: any) => {
        const reqNum = Number(mat?.material_number) || 0
        const materialNo = String(mat?.material_No ?? '').trim()
        if (materialNo && reqNum > 0) total++
      })
    })
  })
  return total
})
const createdCodeCount = computed(() => createdCodes.value.s.length + createdCodes.value.p.length)
const globalStatusText = ref('待执行')
const globalStatusLevel = ref<'idle' | 'info' | 'success' | 'error'>('idle')

const inProgressOrderCodes = ref<string[]>([])
const currentOrderStatus = ref<string>('')
const currentSelectedOrderCode = ref('')
const currentOrderWorkSeqNo = ref('')
const scannerConnected = ref(false)
const scannerRunning = ref(false)
const scannerLastError = ref('')
const scannerLastEventId = ref(0)
const scannerQueue = ref<string[]>([])
const scannerIoSeen = new Set<string>()
let scannerPollTimer: ReturnType<typeof setInterval> | null = null
let scannerConsuming = false
let scannerPollInFlight = false
let scannerBusyQueueNotified = false
const SCANNER_POLL_INTERVAL_MS = 120

const showOrderSelectModal = ref(false)
const selectingOrders = ref<OrderInfo[]>([])
const orderSelectionDraft = ref('')
const orderSelectionReason = ref('')
let orderSelectionResolver: ((code: string | null) => void) | null = null

function addLog(level: 'info' | 'success' | 'warn' | 'error', msg: string) {
  logs.value.unshift({ time: new Date().toLocaleTimeString(), level, msg })
  if (logs.value.length > 200) logs.value.pop()
}

function setGlobalStatus(text: string, level: 'idle' | 'info' | 'success' | 'error' = 'info') {
  globalStatusText.value = text
  globalStatusLevel.value = level
}

function clearScannerAlert() {
  scannerAlertMessage.value = ''
}

function handleMaterialScannerLog(level: 'info' | 'success' | 'warn' | 'error', msg: string) {
  addLog(level, msg)
  if (level === 'warn' || level === 'error') {
    scannerAlertMessage.value = msg
    return
  }
  clearScannerAlert()
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
  scannerAlertMessage.value = ''
  verifiedMaterials.value = []
  createdCodes.value = { s: [], p: [], updatedAt: '' }
  processStartTime.value = new Date().toLocaleString()
  setGlobalStatus('待执行', 'idle')
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
  await restartBarcodeScanner()
  await pollBarcodeScanner().catch(() => {
    // first poll errors are logged in function
  })
  scannerPollTimer = setInterval(() => {
    if (scannerPollInFlight) return
    scannerPollInFlight = true
    pollBarcodeScanner().catch(() => {
      // polling errors are logged in function
    }).finally(() => {
      scannerPollInFlight = false
    })
  }, SCANNER_POLL_INTERVAL_MS)
})

onUnmounted(async () => {
  if (scannerPollTimer) {
    clearInterval(scannerPollTimer)
    scannerPollTimer = null
  }
  scannerPollInFlight = false
})

async function handleScan() {
  const code = productCode.value.trim()
  if (!code || !config.technicsProcessCode) return

  const validation = validateProductBarcode(code)
  if (!validation.ok) {
    testResult.value = 'NG'
    resultMessage.value = validation.message || '产品条码规则校验失败'
    setGlobalStatus(resultMessage.value, 'error')
    addLog('error', `[条码规则] ${resultMessage.value}`)
    productCode.value = ''
    focusScan()
    return
  }

  resetAll()
  orderLoading.value = true
  activeTab.value = 'api'
  addLog('info', `[流程] 新条码 ${code}，先校验首工单状态`)

  try {
    const ok = await ensureOrderSelection('扫码前')
    if (!ok) {
      testResult.value = 'NG'
      resultMessage.value = '工单状态校验未通过，无法继续。'
      setGlobalStatus('工单状态校验失败', 'error')
      return
    }

    if (!orderInfo.value) {
      testResult.value = 'NG'
      resultMessage.value = '未能确定当前工单，请重新确认。'
      setGlobalStatus('工单确认失败', 'error')
      return
    }

    const routeNo = String((orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || '')
    const workSeqNo = (config.technicsProcessCode || '').trim()
    if (!workSeqNo) {
      testResult.value = 'NG'
      resultMessage.value = '配置中工序代码为空，无法下发工步。'
      addLog('error', '[工步] 配置中的工序代码为空，流程停止')
      setGlobalStatus('工步列表获取失败: 工序代码为空', 'error')
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
    setGlobalStatus(`工步列表获取失败: ${routeError.value}`, 'error')
    return
  }

  setGlobalStatus('正在获取工步列表...', 'info')
  routeLoading.value = true
  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: '获取工步',
    url: config.routeApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: { routeCode, workseqNo: workSeqNo }
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
    setGlobalStatus('工步列表获取成功', 'success')
    activeTab.value = 'material'
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    routeError.value = err?.message || '工步查询失败'
    addLog('error', `[工步] 查询失败: ${routeError.value}`)
    setGlobalStatus(`工步列表获取失败: ${routeError.value}`, 'error')
  } finally {
    routeLoading.value = false
  }
}

async function handleMaterialComplete(materials: { productCode: string; productCount: number }[]) {
  if (!orderInfo.value || materialVerificationLoading.value || materialVerificationSuccess.value) return

  clearScannerAlert()
  setGlobalStatus('正在进行单物料校验...', 'info')
  materialVerificationLoading.value = true
  materialVerificationSuccess.value = false
  testResult.value = 'IDLE'
  resultMessage.value = '正在提交单物料校验...'

  const produceOrderCode = (orderInfo.value as any).orderCode || (orderInfo.value as any).order_Code || (orderInfo.value as any).code || ''
  const routeNo = (orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || ''
  const tenantID = getOrderTenantId(orderInfo.value)
  const materialCode = String(materials[0]?.productCode || '').trim() || productCode.value.trim()

  if (!produceOrderCode || !routeNo) {
    testResult.value = 'NG'
    resultMessage.value = '单物料校验失败: 工单或工艺路线为空'
    addLog('error', '[单物料校验] 失败: 工单或工艺路线为空')
    setGlobalStatus('单物料校验失败: 工单或工艺路线为空', 'error')
    materialVerificationLoading.value = false
    return
  }

  if (!materialCode) {
    testResult.value = 'NG'
    resultMessage.value = '单物料校验失败: 物料条码为空'
    addLog('error', '[单物料校验] 失败: 物料条码为空')
    setGlobalStatus('单物料校验失败: 物料条码为空', 'error')
    materialVerificationLoading.value = false
    return
  }

  const fullReqData = {
    produceOrderCode,
    routeNo,
    technicsProcessCode: config.technicsProcessCode,
    tenantID,
    productMixCode: (orderInfo.value as any).productMixCode || (orderInfo.value as any).product_MixCode || null,
    productLine: '',
    materialList: materials
  }
  const singleReqData = {
    produceOrderCode,
    routeNo,
    technicsProcessCode: config.technicsProcessCode,
    materialCode,
    tenantID
  }

  const singleT0 = Date.now()
  const singleRec = reactive<ApiRecord>({
    title: '单物料MES校验',
    url: config.singleMaterialApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: singleReqData
  })
  apiRecords.value.unshift(singleRec)

  try {
    const singleRes = await singleCheckInput(config, singleReqData)
    singleRec.duration = Date.now() - singleT0
    singleRec.resBody = singleRes

    const singleOk = !!(singleRes && (singleRes.code === 200 || singleRes.code === '200' || singleRes.success === true || singleRes.msg === '操作成功'))
    if (!singleOk) {
      const msg = singleRes?.message || singleRes?.msg || '未知错误'
      singleRec.status = 'error'
      testResult.value = 'NG'
      resultMessage.value = `单物料校验未通过: ${msg}`
      addLog('error', `[单物料校验] 失败: ${msg}`)
      setGlobalStatus(`单物料校验失败: ${msg}`, 'error')
      return
    }

    singleRec.status = 'success'
    addLog('success', '[单物料校验] 通过，继续全物料校验')
    setGlobalStatus('单物料校验通过，正在进行全物料校验...', 'info')
    resultMessage.value = '单物料校验通过，正在提交全物料校验...'
  } catch (err: any) {
    singleRec.status = 'error'
    singleRec.resBody = err?.message || String(err)
    testResult.value = 'NG'
    resultMessage.value = `单物料校验请求异常: ${err?.message || String(err)}`
    addLog('error', `[单物料校验] 请求异常: ${err?.message || String(err)}`)
    setGlobalStatus(`单物料校验失败: ${err?.message || String(err)}`, 'error')
    return
  }

  const t0 = Date.now()
  const rec = reactive<ApiRecord>({
    title: '全物料校验',
    url: config.fullMaterialApiUrl,
    status: 'pending',
    time: new Date().toLocaleTimeString(),
    reqBody: fullReqData
  })
  apiRecords.value.unshift(rec)

  try {
    const res = await completeCheckInput(config, fullReqData)
    rec.duration = Date.now() - t0
    rec.resBody = res

    const success = !!(res && (res.code === 200 || res.code === '200' || res.success === true || res.msg === '操作成功'))
    if (!success) {
      const msg = res?.message || res?.msg || '未知错误'
      rec.status = 'error'
      testResult.value = 'NG'
      resultMessage.value = `物料校验未通过: ${msg}`
      addLog('error', `[物料校验] 失败: ${msg}`)
      setGlobalStatus(`物料校验失败: ${msg}`, 'error')
      return
    }

    rec.status = 'success'
    materialVerificationSuccess.value = true
    verifiedMaterials.value = materials
    testResult.value = 'OK'
    resultMessage.value = '物料校验通过，正在获取条码并报工...'
    addLog('success', '[物料校验] 全部通过，开始获取条码并执行报工')
    setGlobalStatus('物料校验通过', 'success')

    await finalizeProcess()
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    testResult.value = 'NG'
    resultMessage.value = `物料校验请求异常: ${err?.message || String(err)}`
    addLog('error', `[物料校验] 请求异常: ${err?.message || String(err)}`)
    setGlobalStatus(`物料校验失败: ${err?.message || String(err)}`, 'error')
  } finally {
    materialVerificationLoading.value = false
  }
}

async function finalizeProcess() {
  const produceInList = await buildProduceInListByCodeCreate()
  if (!produceInList) return
  const mesOk = await submitAllDataToMes(produceInList)
  if (!mesOk) return
  await saveAllLogsToLocal()
  await printGeneratedCodesByBarTender()
}

async function restartBarcodeScanner() {
  try {
    const req = {
      scannerIp: (config.scannerIp || '').trim(),
      scannerPort: Number(config.scannerPort) || 0,
      barcodeRegex: (config.barcodeMatchRegex || '').trim()
    }
    if (!req.scannerIp || req.scannerPort <= 0) {
      addLog('warn', '[扫码枪] IP或端口未配置，后台监听未启动')
      scannerRunning.value = false
      scannerConnected.value = false
      return
    }
    const res = await startBarcodeScanner(req)
    scannerRunning.value = !!res.running
    scannerConnected.value = !!res.connected
    scannerLastError.value = res.lastError || ''
    addLog('info', `[扫码枪] 后台监听已启动: ${req.scannerIp}:${req.scannerPort}，条码匹配规则=${req.barcodeRegex || '(未配置)'}`)
  } catch (err: any) {
    scannerRunning.value = false
    scannerConnected.value = false
    const msg = err?.message || String(err)
    scannerLastError.value = msg
    addLog('error', `[扫码枪] 启动失败: ${msg}`)
  }
}

async function pollBarcodeScanner() {
  try {
    const res = await pullBarcodeScanner(scannerLastEventId.value)
    scannerRunning.value = !!res.running
    scannerConnected.value = !!res.connected
    if (Array.isArray(res.ioLogs)) {
      res.ioLogs.forEach((line) => {
        const text = String(line || '').trim()
        if (!text) return
        if (text.includes('RX BARCODE(')) return
        if (scannerIoSeen.has(text)) return
        scannerIoSeen.add(text)
        if (scannerIoSeen.size > 500) scannerIoSeen.clear()
        addLog('info', text)
      })
    }
    if (res.lastError && res.lastError !== scannerLastError.value) {
      scannerLastError.value = res.lastError
      addLog('warn', `[扫码枪] ${res.lastError}`)
    }

    if (Array.isArray(res.events) && res.events.length) {
      res.events.forEach((ev) => {
        scannerLastEventId.value = Math.max(scannerLastEventId.value, Number(ev.id) || 0)
        const code = String(ev.code || '').trim()
        if (!code) return
        scannerQueue.value.push(code)
        addLog('info', `[扫码枪] 收到条码: ${code}`)
      })
    }
    if (scannerQueue.value.length) await consumeScannerQueue()
  } catch (err: any) {
    const msg = err?.message || String(err)
    if (msg !== scannerLastError.value) {
      scannerLastError.value = msg
      addLog('error', `[扫码枪] 轮询失败: ${msg}`)
    }
  }
}

function resetForNextAutoScan() {
  orderError.value = ''
  routeError.value = ''
  routeSteps.value = []
  materialVerificationSuccess.value = false
  materialVerificationLoading.value = false
  scannerAlertMessage.value = ''
  verifiedMaterials.value = []
  createdCodes.value = { s: [], p: [], updatedAt: '' }
  testResult.value = 'IDLE'
  resultMessage.value = ''
  setGlobalStatus('待执行', 'idle')
  activeTab.value = 'route'
}

async function consumeScannerQueue() {
  if (scannerConsuming) return
  scannerConsuming = true
  try {
    while (scannerQueue.value.length) {
      if (orderLoading.value || routeLoading.value || materialVerificationLoading.value) {
        if (!scannerBusyQueueNotified) {
          addLog('info', '[扫码枪] 已收到条码，当前流程处理中，结束后会自动处理下一条')
          scannerBusyQueueNotified = true
        }
        break
      }
      scannerBusyQueueNotified = false

      const nextCode = scannerQueue.value.shift()
      if (!nextCode) continue

      if (testResult.value === 'NG' && (productCode.value.trim() || routeSteps.value.length > 0)) {
        addLog('warn', `[扫码枪] 当前流程为失败状态，忽略新条码: ${nextCode}`)
        continue
      }

      if (productCode.value.trim() && testResult.value === 'OK') {
        resetForNextAutoScan()
      }

      if (productCode.value.trim() && testResult.value !== 'IDLE') {
        addLog('warn', `[扫码枪] 当前流程未结束，忽略新条码: ${nextCode}`)
        continue
      }

      productCode.value = nextCode
      await handleScan()
    }
  } finally {
    scannerConsuming = false
  }
}

async function submitAllDataToMes(produceInList: Array<{ productCode: string; productCount: number }>): Promise<boolean> {
  if (!orderInfo.value) return false

  setGlobalStatus('正在报工...', 'info')
  const t0 = Date.now()
  const nowDate = new Date().toLocaleDateString()
  const endTimeStr = new Date().toLocaleString()

  const payload = {
    produceOrderCode: (orderInfo.value as any).orderCode || (orderInfo.value as any).order_Code || (orderInfo.value as any).code || '',
    routeNo: (orderInfo.value as any).route_No || (orderInfo.value as any).routeNo || '',
    technicsProcessCode: config.technicsProcessCode,
    technicsProcessName: config.technicsProcessName || '',
    technicsStepCode: 'STEP1',
    technicsStepName: '物料绑定',
    productCode: productCode.value,
    productCount: produceInList.length,
    productQuality: 0,
    produceDate: nowDate,
    startTime: processStartTime.value,
    endTime: endTimeStr,
    userName: config.userName || currentUser.value?.username || 'admin',
    userAccount: config.userAccount || currentUser.value?.username || 'admin',
    deviceCode: config.deviceCode || '',
    Remarks: '',
    ProduceInEntityList: produceInList.map((m) => ({
      productCode: m.productCode,
      ProductCount: m.productCount
    })),
    produceParamEntityList: [],
    ngEntityList: [],
    cellParamEntityList: [],
    otherParamEntityList: [],
    deviceName: config.deviceName || ''
  }

  const finalPayload = [payload]
  const rec = reactive<ApiRecord>({
    title: 'MES 报工上传',
    url: config.mesPushApiUrl,
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
      setGlobalStatus(`报工失败: ${failMsg}`, 'error')
      return false
    }

    rec.status = 'success'
    testResult.value = 'OK'
    resultMessage.value = '报工成功，正在写入日志并执行打印...'
    addLog('success', '[报工] 已成功推送 MES')
    setGlobalStatus('报工成功，处理中...', 'info')
    return true
  } catch (err: any) {
    rec.status = 'error'
    rec.resBody = err?.message || String(err)
    testResult.value = 'NG'
    resultMessage.value = `报工网络异常: ${err?.message || String(err)}`
    addLog('error', `[报工] 网络异常: ${err?.message || String(err)}`)
    setGlobalStatus(`报工失败: ${err?.message || String(err)}`, 'error')
    return false
  }
}

function collectPrintLabels(): PrintLabelItem[] {
  const labels: PrintLabelItem[] = []
  createdCodes.value.s.forEach((code) => {
    labels.push({ code, type: 'S', typeName: '电池系统' })
  })
  createdCodes.value.p.forEach((code) => {
    labels.push({ code, type: 'P', typeName: '电池包' })
  })
  return labels
}

async function printGeneratedCodesByBarTender() {
  const labels = collectPrintLabels()
  if (!labels.length) {
    addLog('warn', '[打印] 未找到可打印条码，跳过打印')
    setGlobalStatus('报工完成（未打印）', 'success')
    resultMessage.value = '物料工站流程完成，报工与日志已完成。'
    return
  }

  const reqBody = {
    barTenderExePath: (config.barTenderExePath || '').trim(),
    templatePath: (config.barTenderTemplatePath || '').trim(),
    databasePath: (config.barTenderDatabasePath || '').trim(),
    labels
  }

  if (!reqBody.barTenderExePath || !reqBody.templatePath || !reqBody.databasePath) {
    addLog('error', '[打印] BarTender 路径配置不完整（EXE/模板/数据库）')
    setGlobalStatus('报工完成，打印失败', 'error')
    resultMessage.value = '报工成功，但打印失败：BarTender 路径配置不完整。'
    return
  }

  setGlobalStatus('正在调用 BarTender 打印...', 'info')

  try {
    const res = await printLabelsByBarTender(reqBody)
    if (!res?.success) {
      const msg = res?.message || 'BarTender 返回失败'
      addLog('error', `[打印] 失败: ${msg}`)
      setGlobalStatus('报工完成，打印失败', 'error')
      resultMessage.value = `报工成功，但打印失败: ${msg}`
      return
    }

    addLog('success', `[打印] 打印成功，条码数量: ${labels.length}`)
    setGlobalStatus('工步列表获取成功，物料校验通过，条码获取成功，报工完成，打印完成', 'success')
    resultMessage.value = '物料工站流程完成，报工、日志、打印均成功。'
  } catch (err: any) {
    addLog('error', `[打印] 请求异常: ${err?.message || String(err)}`)
    setGlobalStatus('报工完成，打印失败', 'error')
    resultMessage.value = `报工成功，但打印请求异常: ${err?.message || String(err)}`
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
  createdCodes.value = { s: [], p: [], updatedAt: '' }
  testResult.value = 'IDLE'
  resultMessage.value = ''
  setGlobalStatus('待执行', 'idle')
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

    <div class="global-status-bar" :class="globalStatusLevel">
      <span class="gs-label">全局状态</span>
      <span class="gs-text">{{ globalStatusText }}</span>
    </div>

    <main class="app-main">
      <section class="left-panel">
        <div class="card scan-card">
          <div class="card-title">1. 扫描产品条码</div>
          <div class="scan-input-wrap" :class="{ scanning: orderLoading }">
            <input
              ref="scanInputRef"
              v-model="productCode"
              type="text"
              placeholder="仅允许扫码枪输入产品条码"
              class="scan-input"
              :disabled="orderLoading || routeLoading"
              readonly
            />
            <button class="scan-btn" disabled>
              仅扫码枪
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
          <button class="tab-btn" :class="{ active: activeTab === 'material' }" @click="activeTab = 'material'">
            物料校验
            <span v-if="materialTaskCount" class="tab-count">{{ materialTaskCount }}</span>
          </button>
          <button class="tab-btn" :class="{ active: activeTab === 'code' }" @click="activeTab = 'code'">
            条码获取
            <span v-if="createdCodeCount" class="tab-count">{{ createdCodeCount }}</span>
          </button>
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
            <div v-if="scannerAlertMessage" class="status-banner fail-mini">{{ scannerAlertMessage }}</div>
            <div v-if="testResult === 'NG'" class="status-banner fail-mini">校验或报工失败: {{ resultMessage }}</div>
            <MaterialScanner
              :steps="routeSteps"
              :remote-check-passed="materialVerificationSuccess"
              :product-code="productCode"
              @log="handleMaterialScannerLog"
              @complete="handleMaterialComplete"
            />
          </div>

          <div v-show="activeTab === 'code'" class="tab-pane code-pane">
            <div v-if="!createdCodeCount" class="code-empty">暂无条码。请先完成全物料校验并获取条码。</div>
            <div v-else class="code-grid">
              <div class="code-item">
                <div class="code-label">S码（电池系统）</div>
                <div class="code-value mono">{{ createdCodes.s.join(', ') }}</div>
              </div>
              <div class="code-item">
                <div class="code-label">P码（电池包）</div>
                <div class="code-value mono">{{ createdCodes.p.join(', ') }}</div>
              </div>
              <div class="code-time">获取时间：{{ createdCodes.updatedAt || '--' }}</div>
            </div>
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

.global-status-bar {
  height: 38px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 16px;
  border-bottom: 1px solid rgba(100, 181, 246, 0.14);
  font-size: 13px;
}

.global-status-bar .gs-label {
  color: #90caf9;
  font-weight: 700;
}

.global-status-bar .gs-text {
  color: #cfd8dc;
}

.global-status-bar.idle {
  background: rgba(120, 144, 156, 0.08);
}

.global-status-bar.info {
  background: rgba(33, 150, 243, 0.12);
}

.global-status-bar.success {
  background: rgba(0, 230, 118, 0.12);
}

.global-status-bar.success .gs-text {
  color: #a5d6a7;
}

.global-status-bar.error {
  background: rgba(244, 67, 54, 0.14);
}

.global-status-bar.error .gs-text {
  color: #ef9a9a;
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

.code-pane {
  padding: 12px;
}

.code-empty {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #546e7a;
  border: 1px dashed rgba(100, 181, 246, 0.2);
  border-radius: 8px;
  background: #0d1117;
  font-size: 12px;
}

.code-grid {
  display: grid;
  gap: 10px;
}

.code-item {
  border: 1px solid rgba(100, 181, 246, 0.15);
  border-radius: 8px;
  background: #0d1117;
  padding: 10px 12px;
}

.code-label {
  font-size: 12px;
  color: #90caf9;
  margin-bottom: 6px;
}

.code-value {
  color: #80cbc4;
  font-size: 13px;
  word-break: break-all;
}

.code-time {
  color: #78909c;
  font-size: 12px;
  padding: 0 2px;
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
