import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import { queryClient } from "./lib/query-client";
import { MantineRoot } from "@/components/mantine-root";
import { TooltipProvider } from "@/components/ui/tooltip";
import "./styles/index.css";
import "./i18n";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <MantineRoot>
        {/* Radix tooltip provider stays until the last shadcn components are migrated (see docs/implementation/mantine-gecis.md). */}
        <TooltipProvider delayDuration={300}>
          <App />
        </TooltipProvider>
      </MantineRoot>
    </QueryClientProvider>
  </React.StrictMode>
);
