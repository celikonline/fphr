import { ReactNode } from "react";
import { useUIStore } from "@/store/ui.store";

interface AppLayoutProps {
  children: ReactNode;
}

/**
 * Main application layout wrapper
 *
 * This layout provides:
 * - Sidebar navigation (Item 20)
 * - Top header with user menu (Item 20)
 * - Main content area
 * - Notification system
 *
 * Item 20 will implement the sidebar and header components
 */
export default function AppLayout({ children }: AppLayoutProps) {
  const sidebarOpen = useUIStore((state) => state.sidebarOpen);
  const toggleSidebar = useUIStore((state) => state.toggleSidebar);

  return (
    <div className="flex h-screen bg-background text-foreground">
      {/* Sidebar placeholder - Item 20 */}
      <aside
        className={`bg-card border-r border-border transition-all duration-300 ${
          sidebarOpen ? "w-64" : "w-16"
        }`}
      >
        {/* Navigation items will be added in Item 20 */}
      </aside>

      {/* Main content area */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Header placeholder - Item 20 */}
        <header className="bg-card border-b border-border">
          <div className="px-6 py-4 flex items-center justify-between">
            <button
              onClick={toggleSidebar}
              className="p-2 hover:bg-accent rounded-md"
              aria-label="Toggle sidebar"
            >
              {/* Menu icon will be added in Item 20 */}
            </button>
            {/* User menu will be added in Item 20 */}
          </div>
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-auto">
          {children}
        </main>
      </div>
    </div>
  );
}
