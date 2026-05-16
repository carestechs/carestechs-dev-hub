import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

interface Block { kind: 'h1' | 'h2' | 'h3' | 'p' | 'ul' | 'code'; text: string; items?: string[]; lang?: string; }

/**
 * Hand-rolled minimal Markdown renderer. v1: headings (#/##/###), bullet lists (-/*),
 * fenced code blocks (```), paragraphs. Other syntax falls through as plain text.
 * Intentionally no external lib — the v1 surface is small and we want zero runtime dependencies.
 */
@Component({
  selector: 'markdown-renderer',
  standalone: true,
  templateUrl: './markdown-renderer.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MarkdownRenderer {
  readonly source = input<string>('');

  protected readonly blocks = computed<Block[]>(() => parse(this.source()));
}

function parse(src: string): Block[] {
  const lines = (src ?? '').split('\n');
  const blocks: Block[] = [];
  let i = 0;
  let inCode = false;
  let codeBuffer: string[] = [];
  let codeLang = '';
  let paragraph: string[] = [];
  let list: string[] | null = null;

  const flushParagraph = () => {
    if (paragraph.length) {
      blocks.push({ kind: 'p', text: paragraph.join(' ') });
      paragraph = [];
    }
  };
  const flushList = () => {
    if (list && list.length) {
      blocks.push({ kind: 'ul', text: '', items: list });
      list = null;
    }
  };

  while (i < lines.length) {
    const line = lines[i];

    if (inCode) {
      if (line.startsWith('```')) {
        blocks.push({ kind: 'code', text: codeBuffer.join('\n'), lang: codeLang });
        codeBuffer = [];
        codeLang = '';
        inCode = false;
      } else {
        codeBuffer.push(line);
      }
      i++; continue;
    }
    if (line.startsWith('```')) {
      flushParagraph(); flushList();
      inCode = true;
      codeLang = line.slice(3).trim();
      i++; continue;
    }

    if (line.startsWith('### ')) {
      flushParagraph(); flushList();
      blocks.push({ kind: 'h3', text: line.slice(4) });
      i++; continue;
    }
    if (line.startsWith('## ')) {
      flushParagraph(); flushList();
      blocks.push({ kind: 'h2', text: line.slice(3) });
      i++; continue;
    }
    if (line.startsWith('# ')) {
      flushParagraph(); flushList();
      blocks.push({ kind: 'h1', text: line.slice(2) });
      i++; continue;
    }

    const listMatch = /^[-*]\s+(.*)/.exec(line);
    if (listMatch) {
      flushParagraph();
      list ??= [];
      list.push(listMatch[1]);
      i++; continue;
    } else if (list) {
      flushList();
    }

    if (line.trim().length === 0) {
      flushParagraph();
    } else {
      paragraph.push(line);
    }
    i++;
  }

  flushParagraph(); flushList();
  if (inCode) blocks.push({ kind: 'code', text: codeBuffer.join('\n'), lang: codeLang });
  return blocks;
}
