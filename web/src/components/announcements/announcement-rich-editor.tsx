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
import { toast } from "@/hooks/use-toast";
import { uploadAnnouncementAttachment } from "@/services/announcements.service";
import { cleanPastedWordHtml } from "@/lib/word-paste-cleanup";

/** Tiptap's Image extension only serializes src/alt/title by default; this keeps data-attachment-id through getHTML()/parseHTML() round-trips (see AnnouncementContentSanitizer's allowlist on the server). */
const AttachmentImage = TiptapImage.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      "data-attachment-id": {
        default: null,
        parseHTML: (element) => element.getAttribute("data-attachment-id"),
        renderHTML: (attributes) =>
          attributes["data-attachment-id"] ? { "data-attachment-id": attributes["data-attachment-id"] as string } : {},
      },
    };
  },
});

const MAX_IMAGE_BYTES = 10 * 1024 * 1024;

interface AnnouncementRichEditorProps {
  /** Null while the duyuru hasn't been saved yet (a brand-new draft) - inline image upload needs an id to attach to. */
  announcementId: string | null;
  content: string;
  onChange: (html: string) => void;
  editable?: boolean;
}

/**
 * Full-page duyuru içeriği editörü (Mantine RichTextEditor / Tiptap): kalın/italik/altı çizili, başlıklar, listeler,
 * link, hizalama ve gömülü resim. Resim yapıştırma/sürükleme doğrudan yakalanır ve `AnnouncementAttachment` olarak
 * yüklenir (bkz. announcements.service.ts) - içerikte asla base64 gömülmez, yalnızca `data-attachment-id` referansı
 * tutulur (sunucu tarafı `AnnouncementContentSanitizer`'ın izin verdiği tek `img` özniteliği budur). Word'den
 * yapıştırılan biçimli metin Tiptap'ın ProseMirror tabanlı yapıştırma ayrıştırıcısından geçer (kalın/italik/liste/
 * başlık korunur); `cleanPastedWordHtml` yalnızca Office'e özgü gürültüyü (mso-* stiller, koşullu yorumlar) temizler.
 */
export function AnnouncementRichEditor({ announcementId, content, onChange, editable = true }: AnnouncementRichEditorProps) {
  const { t } = useTranslation(["announcements", "common"]);

  const editor = useEditor({
    extensions: [
      StarterKit,
      Underline,
      TiptapLink.configure({ openOnClick: false }),
      AttachmentImage,
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      Placeholder.configure({ placeholder: t("announcements:editor.contentPlaceholder") }),
    ],
    content,
    editable,
    onUpdate: ({ editor: current }) => onChange(current.getHTML()),
    editorProps: {
      transformPastedHTML: cleanPastedWordHtml,
      handlePaste: (_view, event) => handleImageFiles(Array.from(event.clipboardData?.files ?? [])),
      handleDrop: (_view, event) => handleImageFiles(Array.from((event as DragEvent).dataTransfer?.files ?? [])),
    },
  });

  // Yalnızca dışarıdan (ör. sunucudan yüklenen mevcut taslak) içerik değişince editörü senkronize eder, her tuş
  // vuruşunda değil - editörün kendi onUpdate'i zaten dışarıya yazıyor, burada geri okumak imleci sıçratır.
  useEffect(() => {
    if (editor && content !== editor.getHTML()) {
      editor.commands.setContent(content, { emitUpdate: false });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [content]);

  function handleImageFiles(files: File[]): boolean {
    const image = files.find((file) => file.type.startsWith("image/"));
    if (!image || !editor) {
      return false;
    }

    if (!announcementId) {
      toast({ variant: "destructive", title: t("announcements:editor.saveDraftFirstTitle"), description: t("announcements:editor.saveDraftFirstHint") });
      return true;
    }

    if (image.size > MAX_IMAGE_BYTES) {
      toast({ variant: "destructive", title: t("common:error"), description: t("announcements:editor.imageTooLarge") });
      return true;
    }

    void uploadAnnouncementAttachment(announcementId, image, "inline")
      .then((attachment) => {
        editor.chain().focus().setImage({ src: attachment.downloadUrl, alt: attachment.fileName, "data-attachment-id": attachment.id } as never).run();
      })
      .catch(() => {
        toast({ variant: "destructive", title: t("common:error"), description: t("announcements:editor.imageUploadFailed") });
      });
    return true;
  }

  return (
    <RichTextEditor editor={editor}>
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
      <RichTextEditor.Content mih={280} />
    </RichTextEditor>
  );
}
