import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { RichTextEditor } from "@mantine/tiptap";
import { useEditor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Underline from "@tiptap/extension-underline";
import TiptapLink from "@tiptap/extension-link";
import TiptapImage from "@tiptap/extension-image";
import TextAlign from "@tiptap/extension-text-align";
import Placeholder from "@tiptap/extension-placeholder";

const MAX_IMAGE_BYTES = 2 * 1024 * 1024;

interface MessageRichEditorProps {
  content: string;
  onChange: (html: string, text: string) => void;
  maxTextLength: number;
}

/** Rich message body editor. Clipboard images are embedded as data URLs so a new message can use them before send. */
export default function MessageRichEditor({ content, onChange, maxTextLength }: MessageRichEditorProps) {
  const { t } = useTranslation("messages");
  const editor = useEditor({
    extensions: [
      StarterKit.configure({ link: false, underline: false }),
      Underline,
      TiptapLink.configure({ openOnClick: false }),
      TiptapImage,
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      Placeholder.configure({ placeholder: t("compose.bodyPlaceholder") }),
    ],
    content,
    onUpdate: ({ editor: current }) => onChange(current.getHTML(), current.getText()),
    editorProps: {
      attributes: {
        role: "textbox",
        "aria-label": t("compose.body"),
        "aria-multiline": "true",
        maxLength: String(maxTextLength),
      },
      handlePaste: (_view, event) => {
        const image = Array.from(event.clipboardData?.items ?? []).find(
          (item) => item.kind === "file" && item.type.startsWith("image/")
        );
        if (!image) {
          const plainText = event.clipboardData?.getData("text/plain");
          if (plainText) {
            editor?.commands.insertContent(plainText);
            return true;
          }
          return false;
        }

        const file = image.getAsFile();
        if (!file || file.size > MAX_IMAGE_BYTES) return true;

        const reader = new FileReader();
        reader.onload = () => {
          if (typeof reader.result !== "string") return;
          editor
            ?.chain()
            .focus()
            .setImage({ src: reader.result, alt: t("compose.pastedImageAlt") })
            .run();
        };
        reader.readAsDataURL(file);
        return true;
      },
    },
  });

  useEffect(() => {
    if (editor && content !== editor.getHTML()) editor.commands.setContent(content, { emitUpdate: false });
  }, [content, editor]);

  return (
    <div>
      <RichTextEditor editor={editor} aria-label={t("compose.body")}>
        <RichTextEditor.Toolbar sticky stickyOffset={0}>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Bold />
            <RichTextEditor.Italic />
            <RichTextEditor.Underline />
            <RichTextEditor.Strikethrough />
            <RichTextEditor.ClearFormatting />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.H1 />
            <RichTextEditor.H2 />
            <RichTextEditor.H3 />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.BulletList />
            <RichTextEditor.OrderedList />
            <RichTextEditor.Blockquote />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Link />
            <RichTextEditor.Unlink />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.AlignLeft />
            <RichTextEditor.AlignCenter />
            <RichTextEditor.AlignRight />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Undo />
            <RichTextEditor.Redo />
          </RichTextEditor.ControlsGroup>
        </RichTextEditor.Toolbar>
        <RichTextEditor.Content
          mih={220}
        />
      </RichTextEditor>
    </div>
  );
}
