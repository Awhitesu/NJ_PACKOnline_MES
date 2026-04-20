export interface AppConfig {
  orderApiUrl: string
  routeApiUrl: string
  technicsProcessCode: string
  logSavePath?: string
  adminUsername?: string
  adminPassword?: string
}

export interface GetOrderRequest {
  produce_Type: number
  tenantID: string
}

export interface OrderInfo {
  orderCode?: string
  code?: string
  order_status?: number | string
  route_No?: string
  workSeqNo?: string
  specsCode?: string
  cell_Level?: string | null
  cell_Batch?: string | null
  productMixCode?: string | null
  projectCode?: string
  productline_no?: string
  polarity?: number
  relateOrderNo?: string
  moduleSort?: string
  orderType?: number
  [key: string]: unknown
}

export interface GetOrderResponse {
  code: number | string
  message?: string
  msg?: string
  datas?: OrderInfo[]
  data?: OrderInfo | OrderInfo[] | null
  success?: boolean
}

export interface OrderStatusSelectionState {
  selectedCode: string
  orderStatus: string
  updatedAt: string
}

export interface GetRouteRequest {
  routeCode: string
  workSeqNo: string
}

export interface RouteStep {
  workseqNo?: string
  workseqName?: string
  sortCode?: number
  routeId?: string
  workseqId?: string
  workSeqParamList?: unknown[]
  workSeqMaterialList?: unknown[]
  workStepList?: WorkStep[]
  [key: string]: unknown
}

export interface WorkStepParam {
  paramName?: string
  minQualityValue?: string | number | null
  maxQualityValue?: string | number | null
  standardValue?: string | number | null
  paramUnit?: string
  [key: string]: unknown
}

export interface WorkStep {
  workstepNo?: string
  workstepName?: string
  workstepType?: number
  docUrl?: string | null
  sortCode?: number
  remark?: string | null
  workStepParamList?: WorkStepParam[]
  workStepMaterialList?: unknown[]
  workStepDocList?: unknown[]
  workStepLineList?: unknown[]
  [key: string]: unknown
}

export interface RouteData {
  workSeqList?: RouteStep[]
  [key: string]: unknown
}

export interface GetRouteResponse {
  code: number | string
  message?: string
  msg?: string
  data?: RouteData | RouteStep[] | null
  success?: boolean
}

export type TestResult = 'IDLE' | 'OK' | 'NG'

export interface MaterialItem {
  productCode: string
  productCount: number
}

export interface CompleteCheckInputRequest {
  produceOrderCode: string
  routeNo: string
  technicsProcessCode: string
  tenantID: string
  productMixCode: string | null
  productLine: string
  materialList: MaterialItem[]
}

export type UserRole = 'admin' | 'operator'

export interface User {
  username: string
  role: UserRole
}

export interface MesSubmission {
  produceOrderCode: string
  routeNo: string
  technicsProcessCode: string
  technicsProcessName: string
  technicsStepCode: string
  technicsStepName: string
  productCode: string
  productCount: number
  productQuality: number
  produceDate: string
  startTime: string
  endTime: string
  userName: string
  userAccount: string
  deviceCode: string
  Remarks: string
  ProduceInEntityList: Array<{
    productCode: string
    ProductCount: number
  }>
  produceParamEntityList: any[]
  ngEntityList: any[]
  cellParamEntityList: any[]
  otherParamEntityList: any[]
  deviceName: string
}
