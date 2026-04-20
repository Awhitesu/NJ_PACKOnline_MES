<script setup lang="ts">
import { ref, reactive, watch, nextTick } from 'vue'
import type { User } from '../types/mes'

const props = defineProps<{
  visible: boolean
  title?: string
  adminUser?: string
  adminPass?: string
}>()

const emit = defineEmits<{
  (e: 'update:visible', val: boolean): void
  (e: 'auth-success', user: User): void
}>()

const form = reactive({
  username: '',
  password: ''
})

const errorMsg = ref('')
const passwordInput = ref<HTMLInputElement | null>(null)

// 当弹窗显示时，自动填充用户名并聚焦密码框
watch(() => props.visible, (newVal) => {
  if (newVal) {
    form.username = props.adminUser?.trim() || 'admin'
    form.password = ''
    errorMsg.value = ''
    nextTick(() => {
      passwordInput.value?.focus()
      passwordInput.value?.select()
    })
  }
})

function handleLogin() {
  const targetUser = props.adminUser?.trim() || 'admin'
  const targetPass = props.adminPass?.trim() || '123'
  
  const isMatch = form.username === targetUser && 
                  form.password === targetPass
                  
  if (isMatch) {
    errorMsg.value = ''
    emit('auth-success', { username: form.username, role: 'admin' })
    emit('update:visible', false)
    form.username = ''
    form.password = ''
  } else {
    errorMsg.value = '管理员用户名或密码错误'
  }
}



function handleCancel() {
  emit('update:visible', false)
  errorMsg.value = ''
}
</script>

<template>
  <Transition name="modal">
    <div v-if="visible" class="login-overlay">

      <div class="login-panel">
        <div class="login-header">
          <span class="icon">🔐</span>
          <h2>{{ title || '管理权限验证' }}</h2>
        </div>

        <div class="login-body">
          <p class="hint">该操作需要管理员权限，请验证身份：</p>
          
          <div class="field">
            <label>账号</label>
            <input 
              v-model="form.username" 
              type="text" 
              placeholder="请输入管理员账号"
              @keyup.enter="handleLogin"
            />
          </div>

          <div class="field">
            <label>密码</label>
            <input 
              ref="passwordInput"
              v-model="form.password" 
              type="password" 
              placeholder="请输入密码"
              @keyup.enter="handleLogin"
            />
          </div>

          <div v-if="errorMsg" class="error-text">
            <span>❌</span> {{ errorMsg }}
          </div>
        </div>

        <div class="login-footer">
          <button class="btn-cancel" @click="handleCancel">取消</button>
          <button class="btn-confirm" @click="handleLogin">验证并执行</button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.login-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.75);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.login-panel {
  background: #1a1f2e;
  border: 1px solid rgba(100, 181, 246, 0.3);
  border-radius: 12px;
  width: 380px;
  box-shadow: 0 32px 64px rgba(0, 0, 0, 0.6);
  overflow: hidden;
}

.login-header {
  padding: 20px;
  background: linear-gradient(135deg, #b71c1c 0%, #d32f2f 100%);
  display: flex;
  align-items: center;
  gap: 12px;
  color: white;
}

.login-header h2 {
  font-size: 16px;
  margin: 0;
  font-weight: 600;
}

.login-body {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.hint {
  font-size: 13px;
  color: #90caf9;
  margin-bottom: 8px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field label {
  font-size: 12px;
  font-weight: 600;
  color: #546e7a;
}

.field input {
  background: #0d1117;
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  color: white;
  padding: 10px 12px;
  font-size: 14px;
  outline: none;
}

.field input:focus {
  border-color: #f44336;
}

.error-text {
  font-size: 12px;
  color: #ef5350;
  background: rgba(244, 67, 54, 0.1);
  padding: 8px 12px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.login-footer {
  padding: 16px 24px;
  background: rgba(0, 0, 0, 0.2);
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.btn-cancel {
  padding: 8px 16px;
  background: transparent;
  border: 1px solid #455a64;
  color: #90a4ae;
  border-radius: 4px;
  cursor: pointer;
}

.btn-confirm {
  padding: 8px 20px;
  background: #d32f2f;
  border: none;
  color: white;
  font-weight: 600;
  border-radius: 4px;
  cursor: pointer;
}

.btn-confirm:hover {
  background: #f44336;
}

.modal-enter-active, .modal-leave-active { transition: all 0.3s ease; }
.modal-enter-from, .modal-leave-to { opacity: 0; transform: scale(0.9); }
</style>
