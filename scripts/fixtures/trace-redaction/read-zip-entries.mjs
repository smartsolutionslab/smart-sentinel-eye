// A minimal, **read-only** ZIP reader used only by
// `scripts/scrub-playwright-artifacts.test.mjs` to inspect a trace zip's
// entries before and after scrubbing.
//
// This is deliberately not the scrubber's zip handling. `plan.md` §3 rejected
// hand-rolling the zip format *for the scrubber* — a program that must both
// read and faithfully rewrite an arbitrary Playwright-produced zip — because
// that is real surface to get subtly wrong in a security guard. Reading a
// zip's entries for a test assertion is a much smaller problem (no write
// path, no need to preserve anything Playwright wrote that this reader
// doesn't understand), and using it here means the guard depends on nothing
// beyond Node's own built-in `zlib`, so `pnpm test:guards` needs no browser,
// no `unzip` binary, and no zip devDependency to make its own assertions
// meaningful — before that devDependency exists (`plan.md` §3, phase 4b).
//
// Supports exactly what Playwright 1.62.1 writes: DEFLATE (method 8) and
// STORED (method 0) entries, no zip64, no encryption, no multi-disk archives.
// Anything else throws — deliberately, rather than silently reading nothing.

import { inflateRawSync } from 'node:zlib';

const EOCD_SIGNATURE = 0x06054b50;
const CENTRAL_DIRECTORY_SIGNATURE = 0x02014b50;
const LOCAL_FILE_HEADER_SIGNATURE = 0x04034b50;
const EOCD_MIN_SIZE = 22;
const MAX_COMMENT_LENGTH = 65535;

function findEndOfCentralDirectory(buffer) {
  const searchStart = Math.max(0, buffer.length - EOCD_MIN_SIZE - MAX_COMMENT_LENGTH);
  for (let offset = buffer.length - EOCD_MIN_SIZE; offset >= searchStart; offset -= 1) {
    if (buffer.readUInt32LE(offset) === EOCD_SIGNATURE) {
      return offset;
    }
  }
  throw new Error('not a zip file: no end-of-central-directory record found');
}

/**
 * Reads a zip file's entries into a `Map<string, Buffer>` of entry name to
 * decompressed content. Throws on anything that is not a well-formed
 * (non-zip64, non-encrypted) zip written with STORED or DEFLATE entries.
 *
 * @param {Buffer} buffer
 * @returns {Map<string, Buffer>}
 */
export function readZipEntries(buffer) {
  const eocdOffset = findEndOfCentralDirectory(buffer);
  const totalEntries = buffer.readUInt16LE(eocdOffset + 10);
  const centralDirectoryOffset = buffer.readUInt32LE(eocdOffset + 16);

  if (centralDirectoryOffset === 0xffffffff || totalEntries === 0xffff) {
    throw new Error('zip64 archives are not supported by this read-only test helper');
  }

  const entries = new Map();
  let cursor = centralDirectoryOffset;

  for (let i = 0; i < totalEntries; i += 1) {
    if (buffer.readUInt32LE(cursor) !== CENTRAL_DIRECTORY_SIGNATURE) {
      throw new Error(`malformed central directory record at offset ${cursor}`);
    }

    const compressionMethod = buffer.readUInt16LE(cursor + 10);
    const compressedSize = buffer.readUInt32LE(cursor + 20);
    const fileNameLength = buffer.readUInt16LE(cursor + 28);
    const extraLength = buffer.readUInt16LE(cursor + 30);
    const commentLength = buffer.readUInt16LE(cursor + 32);
    const localHeaderOffset = buffer.readUInt32LE(cursor + 42);
    const fileName = buffer.toString('utf8', cursor + 46, cursor + 46 + fileNameLength);

    entries.set(fileName, readEntryData(buffer, localHeaderOffset, compressionMethod, compressedSize));

    cursor += 46 + fileNameLength + extraLength + commentLength;
  }

  return entries;
}

function readEntryData(buffer, localHeaderOffset, compressionMethod, compressedSize) {
  if (buffer.readUInt32LE(localHeaderOffset) !== LOCAL_FILE_HEADER_SIGNATURE) {
    throw new Error(`malformed local file header at offset ${localHeaderOffset}`);
  }

  const fileNameLength = buffer.readUInt16LE(localHeaderOffset + 26);
  const extraLength = buffer.readUInt16LE(localHeaderOffset + 28);
  const dataStart = localHeaderOffset + 30 + fileNameLength + extraLength;
  const compressedData = buffer.subarray(dataStart, dataStart + compressedSize);

  if (compressionMethod === 0) {
    return Buffer.from(compressedData);
  }
  if (compressionMethod === 8) {
    return inflateRawSync(compressedData);
  }
  throw new Error(`unsupported zip compression method ${compressionMethod}`);
}
