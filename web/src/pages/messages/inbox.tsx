import MessageList from "@/components/messages/message-list";
import { listInbox } from "@/services/hr-messages.service";

export default function InboxTab() {
  return <MessageList scope="inbox" fetchPage={listInbox} />;
}
