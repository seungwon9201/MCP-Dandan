import type { Step } from 'react-joyride'
import { AlertTriangle, Clock } from 'lucide-react'

// MCP Servers description component
const ServerListContent = () => (
  <div>
    <p>List of MCP servers being monitored. You can check each server's status and risk level through icons.</p>
    <div className="mt-3 space-y-2">
      <div className="flex items-center gap-2">
        <AlertTriangle size={14} className="text-red-500" />
        <span>Action Required</span>
      </div>
      <div className="flex items-center gap-2">
        <AlertTriangle size={14} className="text-orange-400" />
        <span>Action Recommended</span>
      </div>
      <div className="flex items-center gap-2">
        <Clock size={14} className="text-gray-500 animate-spin" />
        <span>Scanning</span>
      </div>
    </div>
  </div>
)

// Available Tools description component
const ToolsListContent = () => (
  <div>
    <p>List of tools provided by the server.</p>
    <div className="mt-3 space-y-2">
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-green-500 rounded-sm" />
        <span>Safe</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-yellow-400 rounded-sm" />
        <span>Action Recommended</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-red-500 rounded-sm" />
        <span>Action Required</span>
      </div>
    </div>
  </div>
)

// Chat Messages description component
const ChatPanelContent = () => (
  <div>
    <p>Communication history with MCP servers.</p>
    <div className="mt-3 space-y-2">
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-blue-100 rounded-full" />
        <span>Client Request</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-gray-200 rounded-full" />
        <span>Server Response</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 bg-yellow-100 rounded-full" />
        <span>Proxy Event</span>
      </div>
    </div>
    <p className="mt-3 text-sm text-gray-600">You can check the risk level by the dot color at the bottom of each message.</p>
  </div>
)

// Dashboard tutorial steps
export const dashboardSteps: Step[] = [
  {
    target: '[data-tutorial="dashboard-btn"]',
    content: 'The dashboard provides an overview of the entire security status at a glance. You can view detected threats and affected server statistics.',
    title: 'Dashboard',
    disableBeacon: true,
    placement: 'right',
  },
  {
    target: '[data-tutorial="server-list"]',
    content: <ServerListContent />,
    title: 'MCP Servers',
    placement: 'right',
  },
  {
    target: '[data-tutorial="detected-table"]',
    content: 'Detailed information on detected security events. You can check server name, threat type, severity, and navigate to the server using the "Go to" button.',
    title: 'Detected Threats',
    placement: 'bottom',
  },
  {
    target: '[data-tutorial="threat-categories"]',
    content: 'You can check the detection status and the number of affected servers for 5 threat types: Tool Poisoning, Command Injection, Filesystem Exposure, PII Leak, and Data Exfiltration.',
    title: 'Threat Categories',
    placement: 'right',
  },
  {
    target: '[data-tutorial="server-chart"]',
    content: 'Visualizes the number of detected threats per server in a donut chart. You can see at a glance which server is experiencing the most threats.',
    title: 'Detected Threats per Server',
    placement: 'left',
  },
  {
    target: '[data-tutorial="category-chart"]',
    content: 'Displays the number of detected threats by category in a donut chart. You can see which type of threat occurs most frequently.',
    title: 'Threats by Category',
    placement: 'left',
  },
  {
    target: '[data-tutorial="timeline"]',
    content: 'Shows threat detection trends over time in a line chart. You can see when the most security events occurred.\n\nNext, we will explore the MCP server detail view.',
    title: 'Time-Series View',
    placement: 'top',
  },
]

// MCP server detail view tutorial steps
export const serverViewSteps: Step[] = [
  {
    target: '[data-tutorial="server-info"]',
    content: 'Detailed information about the selected MCP server. You can check the server name and connection type (local/remote).',
    title: 'Server Info',
    disableBeacon: true,
    placement: 'bottom',
  },
  {
    target: '[data-tutorial="tools-list"]',
    content: <ToolsListContent />,
    title: 'Available Tools',
    placement: 'bottom',
  },
  {
    target: '[data-tutorial="safety-bar"]',
    content: 'You can manually change the risk level by clicking the color bar on the left of each tool.\n\nGreen (Safe) → Yellow (Action Recommended) → Red (Action Required) cycles, and assessed tools are not rescanned.\n\nWhen set to Red (Action Required), the tool will not be delivered to the Client.',
    title: 'Manual Safety Adjustment',
    placement: 'right',
  },
  {
    target: '[data-tutorial="chat-panel"]',
    content: <ChatPanelContent />,
    title: 'Chat Messages',
    placement: 'left',
  },
  {
    target: '[data-tutorial="message-detail"]',
    content: 'Detailed information about the selected message. You can check the malicious score, detection engine results, parameters, and more.',
    title: 'Message Details',
    placement: 'top',
  },
  {
    target: '[data-tutorial="settings-btn"]',
    content: 'Click this button to view the tutorial again!',
    title: 'Tutorial',
    placement: 'top',
  },
]

export const TUTORIAL_STORAGE_KEY = '82ch-tutorial-completed'
export const TUTORIAL_SERVER_VIEW_KEY = '82ch-tutorial-server-completed'
