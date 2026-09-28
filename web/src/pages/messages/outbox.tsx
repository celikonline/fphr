import MessageList from "@/components/messages/message-list";
import { listOutbox } from "@/services/hr-messages.service";

export default function OutboxTab() {
  return <MessageList scope="outbox" fetchPage={listOutbox} />;
}
