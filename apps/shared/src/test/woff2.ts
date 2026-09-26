/// <reference types="node" />
import type { Buffer } from 'node:buffer';
import { brotliDecompressSync } from 'node:zlib';

/**
 * A minimal WOFF2 reader for test support (spec 261 plan.md §6, issue #2333).
 * Not a production dependency: `fonts.test.ts` (S2, S4b, S5) is its only
 * caller. Parses the WOFF2 header and table directory, brotli-decompresses
 * the single table stream, and exposes exactly the four tables those facts
 * need: `head`, `hhea`, `hmtx` and `cmap` (formats 4 and 12). `glyf`/`loca`
 * are transformed in all twelve committed files; only their lengths are
 * tracked here, never decoded — `hmtx` is the untransformed table this
 * reader actually reads (plan.md §6).
 *
 * Reference: W3C WOFF2 (https://www.w3.org/TR/WOFF2/) and OpenType `head` /
 * `hhea` / `hmtx` / `cmap`.
 */

const SIGNATURE = 0x774f4632; // 'wOF2'
const HEADER_SIZE = 48;

// WOFF2's 63 well-known table tags (index 0..62). Index 63 means "the tag
// follows as four literal bytes" (an "arbitrary" table).
const KNOWN_TABLE_TAGS = [
  'cmap',
  'head',
  'hhea',
  'hmtx',
  'maxp',
  'name',
  'OS/2',
  'post',
  'cvt ',
  'fpgm',
  'glyf',
  'loca',
  'prep',
  'CFF ',
  'VORG',
  'EBDT',
  'EBLC',
  'gasp',
  'hdmx',
  'kern',
  'LTSH',
  'PCLT',
  'VDMX',
  'vhea',
  'vmtx',
  'BASE',
  'GDEF',
  'GPOS',
  'GSUB',
  'EBSC',
  'JSTF',
  'MATH',
  'CBDT',
  'CBLC',
  'COLR',
  'CPAL',
  'SVG ',
  'sbix',
  'acnt',
  'avar',
  'bdat',
  'bloc',
  'bsln',
  'cvar',
  'fdsc',
  'feat',
  'fmtx',
  'fvar',
  'gvar',
  'hsty',
  'just',
  'lcar',
  'mort',
  'morx',
  'opbd',
  'prop',
  'trak',
  'Zapf',
  'Silf',
  'Glat',
  'Gloc',
  'Feat',
  'Sill',
];

export interface Woff2Font {
  head: { unitsPerEm: number };
  hhea: { ascender: number; descender: number; lineGap: number };
  hmtx: { advanceWidth(glyphId: number): number };
  cmap: { codePoints: Set<number>; glyphId(codePoint: number): number | undefined };
}

interface Cursor {
  position: number;
}

interface TableEntry {
  tag: string;
  origLength: number;
  streamLength: number;
}

function readUIntBase128(view: DataView, cursor: Cursor): number {
  let value = 0;
  for (let i = 0; i < 5; i++) {
    const byte = view.getUint8(cursor.position);
    cursor.position += 1;
    if (i === 0 && byte === 0x80) {
      throw new Error('WOFF2 UIntBase128 has a leading zero byte');
    }
    value = (value << 7) | (byte & 0x7f);
    if ((byte & 0x80) === 0) {
      return value >>> 0;
    }
  }
  throw new Error('WOFF2 UIntBase128 exceeds 5 bytes');
}

function readTableDirectory(view: DataView, cursor: Cursor, numTables: number): TableEntry[] {
  const entries: TableEntry[] = [];

  for (let i = 0; i < numTables; i++) {
    const flags = view.getUint8(cursor.position);
    cursor.position += 1;

    const tagIndex = flags & 0x3f;
    const transformVersion = (flags >> 6) & 0x3;
    const tag =
      tagIndex === 0x3f
        ? String.fromCharCode(
            view.getUint8(cursor.position),
            view.getUint8(cursor.position + 1),
            view.getUint8(cursor.position + 2),
            view.getUint8(cursor.position + 3),
          )
        : (KNOWN_TABLE_TAGS[tagIndex] ?? `?${tagIndex}`);
    if (tagIndex === 0x3f) {
      cursor.position += 4;
    }

    const origLength = readUIntBase128(view, cursor);
    const isReTransformable = tag === 'glyf' || tag === 'loca';
    if (!isReTransformable && transformVersion !== 0) {
      throw new Error(
        `WOFF2 table '${tag}' has a nonzero transform version (${transformVersion}), which this reader does ` +
          'not know how to skip — the table directory would desync from here on.',
      );
    }
    const streamLength = isReTransformable && transformVersion === 0 ? readUIntBase128(view, cursor) : origLength;

    entries.push({ tag, origLength, streamLength });
  }

  return entries;
}

function sliceTables(entries: TableEntry[], decompressed: Buffer): Map<string, Buffer> {
  const tables = new Map<string, Buffer>();
  let offset = 0;

  for (const entry of entries) {
    tables.set(entry.tag, decompressed.subarray(offset, offset + entry.origLength));
    offset += entry.streamLength;
  }

  return tables;
}

function requireTable(tables: Map<string, Buffer>, tag: string): Buffer {
  const table = tables.get(tag);
  if (table === undefined) {
    throw new Error(`WOFF2 font has no '${tag}' table`);
  }
  return table;
}

function readCmapFormat4(cmap: Buffer, offset: number, mapping: Map<number, number>): void {
  const segCountX2 = cmap.readUInt16BE(offset + 6);
  const segCount = segCountX2 / 2;
  const endCodesOffset = offset + 14;
  const startCodesOffset = endCodesOffset + segCountX2 + 2;
  const idDeltaOffset = startCodesOffset + segCountX2;
  const idRangeOffsetOffset = idDeltaOffset + segCountX2;

  for (let segment = 0; segment < segCount; segment++) {
    const endCode = cmap.readUInt16BE(endCodesOffset + segment * 2);
    const startCode = cmap.readUInt16BE(startCodesOffset + segment * 2);
    const idDelta = cmap.readInt16BE(idDeltaOffset + segment * 2);
    const idRangeOffset = cmap.readUInt16BE(idRangeOffsetOffset + segment * 2);

    if (startCode === 0xffff && endCode === 0xffff) {
      continue;
    }

    for (let codePoint = startCode; codePoint <= endCode; codePoint++) {
      const glyphId =
        idRangeOffset === 0
          ? (codePoint + idDelta) & 0xffff
          : readGlyphIdViaRangeOffset(cmap, idRangeOffsetOffset, segment, idRangeOffset, codePoint, startCode, idDelta);

      if (glyphId !== 0) {
        mapping.set(codePoint, glyphId);
      }
    }
  }
}

function readGlyphIdViaRangeOffset(
  cmap: Buffer,
  idRangeOffsetOffset: number,
  segment: number,
  idRangeOffset: number,
  codePoint: number,
  startCode: number,
  idDelta: number,
): number {
  const glyphIndexAddress = idRangeOffsetOffset + segment * 2 + idRangeOffset + (codePoint - startCode) * 2;
  const rawGlyphId = cmap.readUInt16BE(glyphIndexAddress);
  return rawGlyphId === 0 ? 0 : (rawGlyphId + idDelta) & 0xffff;
}

function readCmapFormat12(cmap: Buffer, offset: number, mapping: Map<number, number>): void {
  const numGroups = cmap.readUInt32BE(offset + 12);

  for (let i = 0; i < numGroups; i++) {
    const groupOffset = offset + 16 + i * 12;
    const startCharCode = cmap.readUInt32BE(groupOffset);
    const endCharCode = cmap.readUInt32BE(groupOffset + 4);
    const startGlyphId = cmap.readUInt32BE(groupOffset + 8);

    for (let codePoint = startCharCode; codePoint <= endCharCode; codePoint++) {
      mapping.set(codePoint, startGlyphId + (codePoint - startCharCode));
    }
  }
}

function readCmap(tables: Map<string, Buffer>): Woff2Font['cmap'] {
  const cmap = requireTable(tables, 'cmap');
  const numSubtables = cmap.readUInt16BE(2);

  let bestOffset = -1;
  let bestScore = -1;

  for (let i = 0; i < numSubtables; i++) {
    const recordOffset = 4 + i * 8;
    const platformId = cmap.readUInt16BE(recordOffset);
    const subtableOffset = cmap.readUInt32BE(recordOffset + 4);
    const format = cmap.readUInt16BE(subtableOffset);
    const score = format === 12 ? 2 : format === 4 ? 1 : 0;

    if (score > bestScore && (platformId === 3 || platformId === 0)) {
      bestScore = score;
      bestOffset = subtableOffset;
    }
  }

  if (bestOffset < 0) {
    throw new Error('WOFF2 cmap table has no Windows/Unicode format 4 or 12 subtable');
  }

  const mapping = new Map<number, number>();
  const format = cmap.readUInt16BE(bestOffset);

  if (format === 4) {
    readCmapFormat4(cmap, bestOffset, mapping);
  } else {
    readCmapFormat12(cmap, bestOffset, mapping);
  }

  return {
    codePoints: new Set(mapping.keys()),
    glyphId: (codePoint: number) => mapping.get(codePoint),
  };
}

function readHmtx(tables: Map<string, Buffer>): Woff2Font['hmtx'] {
  const numGlyphs = requireTable(tables, 'maxp').readUInt16BE(4);
  const numberOfHMetrics = requireTable(tables, 'hhea').readUInt16BE(34);
  const hmtx = requireTable(tables, 'hmtx');

  const advances: number[] = [];
  for (let i = 0; i < numberOfHMetrics; i++) {
    advances.push(hmtx.readUInt16BE(i * 4));
  }
  const lastAdvance = advances[advances.length - 1] ?? 0;
  for (let i = numberOfHMetrics; i < numGlyphs; i++) {
    advances.push(lastAdvance);
  }

  return {
    advanceWidth: (glyphId: number) => advances[glyphId] ?? lastAdvance,
  };
}

/** Parses one WOFF2 file's bytes into the four tables the fonts tests need. */
export function parseWoff2(bytes: Buffer): Woff2Font {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  if (view.getUint32(0) !== SIGNATURE) {
    throw new Error('not a WOFF2 file: bad signature');
  }

  const numTables = view.getUint16(12);
  const totalCompressedSize = view.getUint32(20);

  const cursor: Cursor = { position: HEADER_SIZE };
  const entries = readTableDirectory(view, cursor, numTables);

  const compressed = bytes.subarray(cursor.position, cursor.position + totalCompressedSize);
  const decompressed = brotliDecompressSync(compressed);
  const tables = sliceTables(entries, decompressed);

  const head = requireTable(tables, 'head');
  const hhea = requireTable(tables, 'hhea');

  return {
    head: { unitsPerEm: head.readUInt16BE(18) },
    hhea: {
      ascender: hhea.readInt16BE(4),
      descender: hhea.readInt16BE(6),
      lineGap: hhea.readInt16BE(8),
    },
    hmtx: readHmtx(tables),
    cmap: readCmap(tables),
  };
}
