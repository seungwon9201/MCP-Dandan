"use strict";
const electron = require("electron");
electron.contextBridge.exposeInMainWorld("electronAPI", {
  // IPC 통신
  ping: () => electron.ipcRenderer.invoke("ping"),
  getAppInfo: () => electron.ipcRenderer.invoke("get-app-info"),
  // Database API
  getServers: () => electron.ipcRenderer.invoke("api:servers"),
  getServerMessages: (serverId) => electron.ipcRenderer.invoke("api:servers:messages", serverId),
  getEngineResults: () => electron.ipcRenderer.invoke("api:engine-results"),
  // 필요에 따라 추가 API 노출
  platform: process.platform,
  versions: {
    node: process.versions.node,
    chrome: process.versions.chrome,
    electron: process.versions.electron
  }
});
