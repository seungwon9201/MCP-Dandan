// TypeScript 타입 정의

export interface MCPTool {
  name: string
  description: string
  safety?: number  // 0: 검사 전, 1: 안전(ALLOW), 2: 위험(DENY)
}

export interface MCPServer {
  id: string | number
  name: string
  type: string
  icon: string
  appName?: string
  tools: MCPTool[]
  isChecking?: boolean  // 검사 중인 도구가 있는가
  hasDanger?: boolean   // 위험한 도구가 있는가
}

export interface ChatMessage {
  id: string | number
  content: string
  timestamp: string
  sender: 'user' | 'assistant' | 'client' | 'server'
  serverId?: string | number
  type?: string
  maliciousScore?: number
  event_type?: string  // 'MCP', 'Proxy' 등
  data?: {
    message?: {
      id?: string | number
      params?: {
        name?: string
        arguments?: Record<string, any>
        [key: string]: any
      }
      [key: string]: any
    }
    [key: string]: any
  }
}

export interface ServerInfo {
  name: string
  type: string
  tools: MCPTool[]
}

export interface DetectedEvent {
  serverName: string
  threatType: string
  severity: 'low' | 'mid' | 'high'
  severityColor: string
  description: string
  lastSeen: string
  engineResultId: string | number
  rawEventId: string | number
}

export interface ThreatStats {
  detections: number
  affectedServers: number
}

export interface TimelineData {
  date: string
  count: number
}

export interface TopServer {
  name: string
  count: number
}
