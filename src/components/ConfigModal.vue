<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { AppConfig } from '../types/mes'

const props = defineProps<{
  modelValue: AppConfig
  visible: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', val: AppConfig): void
  (e: 'update:visible', val: boolean): void
  (e: 'save'): void
}>()

const form = reactive<AppConfig>({ ...props.modelValue })

watch(
  () => props.visible,
  (visible) => {
    if (visible) Object.assign(form, props.modelValue)
  }
)

function handleSave() {
  emit('update:modelValue', { ...form })
  emit('save')
  emit('update:visible', false)
}

function handleCancel() {
  Object.assign(form, props.modelValue)
  emit('update:visible', false)
}
</script>

<template>
  <Transition name="modal">
    <div v-if="visible" class="modal-overlay" @click.self="handleCancel">
      <div class="modal-panel">
        <div class="modal-header">
          <h2>系统配置</h2>
          <button class="close-btn" @click="handleCancel">关闭</button>
        </div>

        <div class="modal-body">
          <section class="config-section">
            <div class="section-title">MES 参数设置</div>
            <div class="field-group">
              <label>首工序获取工单 API</label>
              <input v-model="form.orderApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>获取工步 API</label>
              <input v-model="form.routeApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>单物料校验 API</label>
              <input v-model="form.singleMaterialApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>全物料校验 API</label>
              <input v-model="form.fullMaterialApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>获取条码 API</label>
              <input v-model="form.codeCreateApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>MES 数据上传 API</label>
              <input v-model="form.mesPushApiUrl" type="text" class="input-field" />
            </div>
          </section>

          <section class="config-section">
            <div class="section-title">系统参数设置</div>
            <div class="field-group">
              <label>工序代码 (technicsProcessCode)</label>
              <input v-model="form.technicsProcessCode" type="text" class="input-field" />
            </div>
            <div class="field-group">
              <label>工序名称 (technicsProcessName)</label>
              <input v-model="form.technicsProcessName" type="text" class="input-field" />
            </div>
            <div class="field-groups-row">
              <div class="field-group">
                <label>用户名 (userName)</label>
                <input v-model="form.userName" type="text" class="input-field" />
              </div>
              <div class="field-group">
                <label>用户账号 (userAccount)</label>
                <input v-model="form.userAccount" type="text" class="input-field" />
              </div>
            </div>
            <div class="field-groups-row">
              <div class="field-group">
                <label>设备编码 (deviceCode)</label>
                <input v-model="form.deviceCode" type="text" class="input-field" />
              </div>
              <div class="field-group">
                <label>设备名称 (deviceName)</label>
                <input v-model="form.deviceName" type="text" class="input-field" />
              </div>
            </div>
            <div class="field-group">
              <label>日志保存路径 (后台机器)</label>
              <input v-model="form.logSavePath" type="text" class="input-field" />
            </div>
            <div class="field-groups-row">
              <div class="field-group">
                <label>管理员账号</label>
                <input v-model="form.adminUsername" type="text" class="input-field" />
              </div>
              <div class="field-group">
                <label>管理员密码</label>
                <input v-model="form.adminPassword" type="password" class="input-field" />
              </div>
            </div>
          </section>

          <section class="config-section">
            <div class="section-title">打印机参数设置</div>
            <div class="field-group">
              <label>打印接口 URL</label>
              <input v-model="form.printApiUrl" type="text" class="input-field" />
            </div>
            <div class="field-groups-row">
              <div class="field-group">
                <label>Zebra 打印机 IP</label>
                <input v-model="form.printerIp" type="text" class="input-field" />
              </div>
              <div class="field-group">
                <label>Zebra 端口</label>
                <input v-model.number="form.printerPort" type="number" class="input-field" />
              </div>
            </div>
          </section>
        </div>

        <div class="modal-footer">
          <button class="btn-cancel" @click="handleCancel">取消</button>
          <button class="btn-save" @click="handleSave">保存</button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.65);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-panel {
  background: #1a1f2e;
  border: 1px solid rgba(100, 181, 246, 0.25);
  border-radius: 12px;
  width: 560px;
  max-width: 95vw;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.5);
  overflow: hidden;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 18px 24px;
  background: linear-gradient(135deg, #0d47a1 0%, #1565c0 100%);
  border-bottom: 1px solid rgba(100, 181, 246, 0.2);
}

.modal-header h2 {
  font-size: 16px;
  font-weight: 600;
  color: #e3f2fd;
  margin: 0;
}

.close-btn {
  background: transparent;
  border: 1px solid rgba(255, 255, 255, 0.25);
  color: #e3f2fd;
  border-radius: 4px;
  padding: 4px 8px;
  cursor: pointer;
}

.modal-body {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-height: 70vh;
  overflow: auto;
}

.config-section {
  border: 1px solid rgba(100, 181, 246, 0.16);
  border-radius: 10px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  background: rgba(13, 17, 23, 0.45);
}

.section-title {
  font-size: 14px;
  font-weight: 700;
  color: #90caf9;
  border-left: 3px solid #42a5f5;
  padding-left: 8px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-group label {
  font-size: 13px;
  font-weight: 600;
  color: #90caf9;
}

.input-field {
  background: #0d1117;
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  color: #e0e6ed;
  padding: 10px 14px;
  font-size: 13px;
  font-family: Consolas, monospace;
  outline: none;
}

.input-field:focus {
  border-color: #42a5f5;
  box-shadow: 0 0 0 3px rgba(66, 165, 245, 0.15);
}

.field-groups-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.modal-footer {
  padding: 16px 24px;
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  border-top: 1px solid rgba(100, 181, 246, 0.1);
}

.btn-cancel {
  padding: 9px 20px;
  background: transparent;
  border: 1px solid rgba(100, 181, 246, 0.25);
  border-radius: 6px;
  color: #78909c;
  font-size: 13px;
  cursor: pointer;
}

.btn-save {
  padding: 9px 24px;
  background: linear-gradient(135deg, #1565c0, #0d47a1);
  border: 1px solid rgba(100, 181, 246, 0.3);
  border-radius: 6px;
  color: #e3f2fd;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.modal-enter-active,
.modal-leave-active {
  transition: opacity 0.25s ease;
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

.modal-enter-active .modal-panel,
.modal-leave-active .modal-panel {
  transition: transform 0.25s ease;
}

.modal-enter-from .modal-panel,
.modal-leave-to .modal-panel {
  transform: scale(0.92) translateY(-20px);
}
</style>
