// Electron API 타입 정의

export interface ElectronAPI {
  ping: () => Promise<string>
  getAppInfo: () => Promise<{
    version: string
    name: string
    platform: string
  }>
  getServers: () => Promise<any[]>
  getServerMessages: (serverId: number) => Promise<any[]>
  getEngineResults: () => Promise<any[]>
  platform: string
  versions: {
    node: string
    chrome: string
    electron: string
  }
}

declare global {
  interface Window {
    electronAPI: ElectronAPI
  }
}

export {}
