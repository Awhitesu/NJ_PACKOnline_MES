import type {
  AppConfig,
  GetOrderRequest,
  GetOrderResponse,
  GetRouteRequest,
  GetRouteResponse,
  CompleteCheckInputRequest,
  OrderStatusSelectionState
} from '../types/mes'

async function postRequest<T>(url: string, body: object): Promise<T> {
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json'
    },
    body: JSON.stringify(body)
  })

  if (!response.ok) {
    throw new Error(`HTTP错误: ${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

export async function getOrderByProcess(config: AppConfig): Promise<GetOrderResponse> {
  const params: GetOrderRequest = {
    produce_Type: 3,
    tenantID: 'FD'
  }
  return postRequest<GetOrderResponse>(config.orderApiUrl, params)
}

export async function getRouteList(config: AppConfig, routeCode: string, workSeqNo: string): Promise<GetRouteResponse> {
  const params: GetRouteRequest = {
    routeCode,
    workSeqNo
  }
  return postRequest<GetRouteResponse>(config.routeApiUrl, params)
}

export async function completeCheckInput(_config: AppConfig, data: CompleteCheckInputRequest): Promise<any> {
  return postRequest<any>('/mes-api/api/ProduceMessage/CompleteCheckInput', data)
}

export async function pushPackMessageToMes(_config: AppConfig, data: any[]): Promise<any> {
  return postRequest<any>('/mes-push/api/ProduceMessage/PushPackMessageToMes', data)
}

export async function readOrderStatusSelectionFromFile(): Promise<OrderStatusSelectionState | null> {
  const response = await fetch('http://127.0.0.1:5246/orderStatusSelection')
  if (response.status === 404) return null

  if (!response.ok) {
    throw new Error(`读取状态文件失败: HTTP ${response.status}`)
  }

  const data = (await response.json()) as Partial<OrderStatusSelectionState> & { exists?: boolean }
  if (data.exists === false) return null

  if (!data.selectedCode) return null
  return {
    selectedCode: String(data.selectedCode),
    orderStatus: String(data.orderStatus ?? '下发中'),
    updatedAt: String(data.updatedAt ?? '')
  }
}

export async function saveOrderStatusSelectionToFile(state: OrderStatusSelectionState): Promise<void> {
  const response = await fetch('http://127.0.0.1:5246/orderStatusSelection', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json'
    },
    body: JSON.stringify(state)
  })

  if (!response.ok) {
    throw new Error(`保存状态文件失败: HTTP ${response.status}`)
  }
}
