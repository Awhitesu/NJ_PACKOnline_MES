/**
 * MES API Mock Server
 * 1. GetOtherOrderInfoByProcess
 * 2. GetTechRouteListByCode
 */
const http = require('http')

const PORT = 8076

const mockOrderResponse = {
  code: 200,
  msg: 'success',
  datas: [
    {
      code: 'PACK-2026-0001',
      orderCode: 'PACK-2026-0001',
      order_status: '下发中',
      route_No: 'ROUTE-CTP-99',
      workSeqNo: 'CTP_P1100',
      productMixCode: 'MIX-01'
    },
    {
      code: 'PACK-2026-0002',
      orderCode: 'PACK-2026-0002',
      order_status: '下发中',
      route_No: 'ROUTE-CTP-99',
      workSeqNo: 'CTP_P1100',
      productMixCode: 'MIX-01'
    },
    {
      code: 'PACK-2026-0003',
      orderCode: 'PACK-2026-0003',
      order_status: '已完成',
      route_No: 'ROUTE-CTP-99',
      workSeqNo: 'CTP_P1100',
      productMixCode: 'MIX-02'
    }
  ]
}

const mockRouteResponse = {
  code: 200,
  msg: 'success',
  data: {
    workSeqList: [
      {
        workseqNo: '10',
        workseqName: '物料绑定工序',
        workStepList: [
          {
            workstepNo: '10-1',
            workstepName: '扫码底板条码',
            workstepType: 0,
            workStepMaterialList: [
              { material_No: 'MAT-BASE-001', material_Name: '底板', material_number: 1, noLength: 12 },
              { material_No: 'MAT-COVER-001', material_Name: '上盖', material_number: 1, noLength: 12 }
            ]
          }
        ]
      }
    ]
  }
}

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*')
  res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS')
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type')
  res.setHeader('Content-Type', 'application/json; charset=utf-8')

  if (req.method === 'OPTIONS') {
    res.writeHead(204)
    res.end()
    return
  }

  let body = ''
  req.on('data', (chunk) => {
    body += chunk
  })

  req.on('end', () => {
    let parsed = {}
    try {
      parsed = body ? JSON.parse(body) : {}
    } catch {
      parsed = {}
    }

    console.log(`[Mock MES] ${req.url} | body=${JSON.stringify(parsed)}`)

    if (req.url.includes('GetOtherOrderInfoByProcess')) {
      const produceType = Number(parsed.produce_Type || 0)
      const tenantID = parsed.tenantID
      if (produceType !== 3 || tenantID !== 'FD') {
        res.end(JSON.stringify({ code: 400, msg: '参数错误: produce_Type/tenantID 不匹配' }))
        return
      }

      res.end(JSON.stringify(mockOrderResponse))
      return
    }

    if (req.url.includes('GetTechRouteListByCode')) {
      if (!parsed.routeCode || !parsed.workSeqNo) {
        res.end(JSON.stringify({ code: 400, msg: '参数错误: routeCode/workSeqNo 必填' }))
        return
      }
      res.end(JSON.stringify(mockRouteResponse))
      return
    }

    res.writeHead(404)
    res.end(JSON.stringify({ code: 404, msg: 'Not Found' }))
  })
})

server.listen(PORT, () => {
  console.log('=========================================')
  console.log(' MES API Mock Server')
  console.log(` listen: http://127.0.0.1:${PORT}`)
  console.log(' endpoints:')
  console.log(' - /api/OrderInfo/GetOtherOrderInfoByProcess')
  console.log(' - /api/OrderInfo/GetTechRouteListByCode')
  console.log('=========================================')
})
