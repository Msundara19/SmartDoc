import { Routes, Route, NavLink, useLocation } from 'react-router-dom'
import UploadPage from './pages/UploadPage'
import LibraryPage from './pages/LibraryPage'
import ChatPage from './pages/ChatPage'
import FlashcardsPage from './pages/FlashcardsPage'

const NAV = [
  {
    to: '/',
    exact: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8}
          d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
      </svg>
    ),
    label: 'Upload',
  },
  {
    to: '/library',
    exact: false,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8}
          d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
      </svg>
    ),
    label: 'Library',
  },
]

function Sidebar() {
  const location = useLocation()
  const isChatPage = location.pathname.startsWith('/chat/')

  return (
    <aside
      className={`${isChatPage ? 'w-14' : 'w-56'} shrink-0 flex flex-col border-r border-border bg-surface-1 transition-all duration-300`}
    >
      {/* Logo */}
      <div className="px-4 py-5 flex items-center gap-2.5">
        <div className="w-7 h-7 rounded-lg bg-gradient-brand flex items-center justify-center shrink-0">
          <svg className="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
            <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414A1 1 0 0119 9.414V19a2 2 0 01-2 2z" />
          </svg>
        </div>
        {!isChatPage && (
          <div>
            <span className="text-gradient font-semibold text-sm">SmartDoc</span>
            <span className="block text-muted text-[10px] leading-none mt-0.5">RAG · v1.0</span>
          </div>
        )}
      </div>

      <div className="h-px bg-border mx-3 mb-3" />

      {/* Nav */}
      <nav className="flex flex-col gap-1 px-2">
        {NAV.map(({ to, exact, icon, label }) => (
          <NavLink
            key={to}
            to={to}
            end={exact}
            className={({ isActive }) =>
              `flex items-center gap-3 px-2.5 py-2 rounded-lg text-sm transition-colors
              ${isActive
                ? 'bg-accent-blue/10 text-accent-blue border border-accent-blue/20'
                : 'text-muted hover:text-gray-100 hover:bg-surface-3'
              }`
            }
          >
            {icon}
            {!isChatPage && <span>{label}</span>}
          </NavLink>
        ))}
      </nav>

      <div className="mt-auto px-3 pb-4">
        {!isChatPage && (
          <div className="text-[10px] text-muted/60 space-y-1 pt-4 border-t border-border">
            <p className="flex items-center gap-1.5">
              <span className="w-1.5 h-1.5 rounded-full bg-accent-green inline-block" />
              pgvector · nomic-embed
            </p>
            <p className="flex items-center gap-1.5">
              <span className="w-1.5 h-1.5 rounded-full bg-accent-purple inline-block" />
              Groq · llama-3.3-70b
            </p>
          </div>
        )}
      </div>
    </aside>
  )
}

export default function App() {
  return (
    <div className="min-h-screen flex bg-surface-0">
      <Sidebar />
      <main className="flex-1 min-w-0 overflow-auto">
        <Routes>
          <Route path="/" element={<UploadPage />} />
          <Route path="/library" element={<LibraryPage />} />
          <Route path="/chat/:documentId" element={<ChatPage />} />
          <Route path="/chat/:documentId/flashcards" element={<FlashcardsPage />} />
        </Routes>
      </main>
    </div>
  )
}
