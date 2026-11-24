import { useState, type DragEvent } from "react";

/** The drag carries files, not text or a link. */
export const draggingFiles = (event: DragEvent) => event.dataTransfer.types.includes("Files");

/**
 * Files dropped on an element (a pointer shortcut): `dropHandlers` go on it, `onFiles` gets every
 * file dropped, and `dragging` holds while files are over it. Unless `enabled`, drags pass by.
 */
export function useFileDrop(onFiles: (files: File[]) => void, enabled = true) {
  const [dragging, setDragging] = useState(false);

  const over = (event: DragEvent) => {
    if (!enabled || !draggingFiles(event)) return;
    event.preventDefault();
    setDragging(true);
  };

  const leave = (event: DragEvent) => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
  };

  const drop = (event: DragEvent) => {
    if (!enabled || !draggingFiles(event)) return;
    event.preventDefault();
    setDragging(false);
    onFiles([...event.dataTransfer.files]);
  };

  return {
    dragging,
    dropHandlers: { onDragEnter: over, onDragOver: over, onDragLeave: leave, onDrop: drop },
  };
}
