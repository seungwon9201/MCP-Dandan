const express = require('express')
const cors = require('cors')
const { mcpServers, chatMessagesByServer } = require('./mockData')

const app = express()
const PORT = 3001

// Middleware
app.use(cors())
app.use(express.json())

// API Routes

// Get all MCP servers
app.get('/api/servers', (req, res) => {
  res.json(mcpServers)
})

// Get a single server by ID
app.get('/api/servers/:id', (req, res) => {
  const serverId = parseInt(req.params.id)
  const server = mcpServers.find(s => s.id === serverId)

  if (!server) {
    return res.status(404).json({ error: 'Server not found' })
  }

  res.json(server)
})

// Get messages for a specific server
app.get('/api/servers/:id/messages', (req, res) => {
  const serverId = parseInt(req.params.id)
  const messages = chatMessagesByServer[serverId] || []
  res.json(messages)
})

// Get all messages (for all servers)
app.get('/api/messages', (req, res) => {
  res.json(chatMessagesByServer)
})

app.listen(PORT, () => {
  console.log(`Backend server running on http://localhost:${PORT}`)
})