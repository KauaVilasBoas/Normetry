import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { useConversations, type Citation, type Establishment } from "../api/queries";
import { copy } from "../copy";
import { media, useMediaQuery } from "../ui/media";
import { Sheet } from "../ui/primitives";
import { chipId } from "./answers";
import { COMPOSER_ID } from "./Composer";
import { ConversationList } from "./ConversationList";
import { ConversationPane, SHOW_CONVERSATIONS_ID, TITLE_ID } from "./ConversationPane";
import { SourcePanel } from "./SourcePanel";

const focusById = (id: string) => document.getElementById(id)?.focus();

/**
 * Ask the Regulation: conversation list | conversation | source panel (DESIGN.md §4). `/` is a
 * new conversation, `/conversations/{id}` a saved one. A new `focusKey` (from the shell) moves
 * focus to the conversation heading.
 */
export function ChatScreen({
  establishment,
  conversationId,
  focusKey,
  onEstablishment,
}: {
  establishment: Establishment;
  conversationId: string | undefined;
  focusKey: number;
  /** Selects the establishment of a conversation opened by URL. */
  onEstablishment: (establishment: Establishment) => void;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const conversations = useConversations();
  const listInline = useMediaQuery(media.list);
  const sourceInline = useMediaQuery(media.source);

  // One pane per conversation, so leaving one aborts its answer. Every visit to `/` is a new
  // conversation; the one it creates keeps its pane while the URL takes the new id.
  const [created, setCreated] = useState<{ id: string; paneKey: string }>();
  const paneKey =
    conversationId === undefined
      ? location.key
      : conversationId === created?.id
        ? created.paneKey
        : conversationId;

  // The open source belongs to one pane: it closes when the conversation changes.
  const [source, setSource] = useState<{ citation: Citation; paneKey: string }>();
  const openSource = source?.paneKey === paneKey ? source.citation : undefined;
  const lastChip = useRef<string>(undefined);

  const [listOpen, setListOpen] = useState(false);
  const listSheetOpen = listOpen && !listInline;
  const focusAfterListSheet = useRef<string>(undefined);

  const [focusRequest, setFocusRequest] = useState<{ id: string }>();
  useEffect(() => {
    if (focusRequest) focusById(focusRequest.id);
  }, [focusRequest]);
  useEffect(() => {
    if (focusKey > 0) focusById(TITLE_ID);
  }, [focusKey]);

  const [announcements, setAnnouncements] = useState<{ id: number; text: string; lang?: string }[]>(
    [],
  );
  const announce = (text: string, lang?: string) =>
    setAnnouncements((list) => [...list.slice(-9), { id: (list.at(-1)?.id ?? 0) + 1, text, lang }]);

  /** Focus after a navigation; from the conversations sheet, once it has closed. */
  const focusNext = (id: string) => {
    if (listSheetOpen) {
      focusAfterListSheet.current = id;
      setListOpen(false);
    } else {
      setFocusRequest({ id });
    }
  };

  const startNew = () => {
    void navigate("/");
    focusNext(COMPOSER_ID);
  };

  const toggleSource = (citation: Citation) => {
    lastChip.current = chipId(citation);
    setSource(citation.id === openSource?.id ? undefined : { citation, paneKey });
  };

  const closeSource = () => {
    setSource(undefined);
    // The sheet gives focus back once it has closed (onCloseAutoFocus).
    if (sourceInline && lastChip.current) focusById(lastChip.current);
  };

  const list = (inSheet: boolean) => (
    <ConversationList
      establishment={establishment}
      activeId={conversationId}
      onNew={startNew}
      onOpen={() => focusNext(TITLE_ID)}
      inSheet={inSheet}
    />
  );

  return (
    <div className="flex min-h-0 flex-1 gap-4">
      {listInline && <div className="flex w-60 shrink-0">{list(false)}</div>}

      <ConversationPane
        key={paneKey}
        establishment={establishment}
        conversationId={conversationId}
        knownTitle={conversations.data?.find((item) => item.id === conversationId)?.title}
        compact={!listInline}
        openCitationId={openSource?.id}
        onOpenSource={toggleSource}
        onEstablishment={onEstablishment}
        onCreated={(id) => {
          setCreated({ id, paneKey });
          void navigate(`/conversations/${id}`, { replace: true });
        }}
        onDeleted={() => {
          void navigate("/");
          focusNext(TITLE_ID);
          announce(copy.chat.deleted);
        }}
        onNew={startNew}
        onShowConversations={() => setListOpen(true)}
        announce={announce}
      />

      {sourceInline && openSource && (
        <SourcePanel citation={openSource} onClose={closeSource} inSheet={false} />
      )}

      <Sheet
        side="left"
        open={listSheetOpen}
        onOpenChange={setListOpen}
        className="w-70 max-w-[85vw]"
        // After a choice, focus goes where it leads; otherwise back to Show conversations (Radix
        // only returns focus to a Dialog.Trigger, and this sheet opens from the pane header).
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          focusById(focusAfterListSheet.current ?? SHOW_CONVERSATIONS_ID);
          focusAfterListSheet.current = undefined;
        }}
      >
        {list(true)}
      </Sheet>

      <Sheet
        side="right"
        open={!sourceInline && openSource !== undefined}
        onOpenChange={(open) => {
          if (!open) closeSource();
        }}
        className="w-full sm:w-100"
        // The panel focuses its heading itself; on close, focus goes back to the chip.
        onOpenAutoFocus={(event) => event.preventDefault()}
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          if (lastChip.current) focusById(lastChip.current);
        }}
      >
        {openSource && <SourcePanel citation={openSource} onClose={closeSource} inSheet />}
      </Sheet>

      <div aria-live="polite" className="sr-only">
        {announcements.map((item) => (
          <p key={item.id} lang={item.lang}>
            {item.text}
          </p>
        ))}
      </div>
    </div>
  );
}
