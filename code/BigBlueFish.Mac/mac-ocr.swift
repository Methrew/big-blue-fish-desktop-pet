// On-device Apple Vision OCR. Reads one image on stdin and prints tesseract's TSV layout
// (level 5 rows), so the pet parses it exactly as the Linux tesseract output.
// Taiwan edition: Traditional Chinese is read first, then Simplified Chinese and English. Lines without any CJK
// character are read again by an English-only pass, which spells Latin UI text (API, llama, ASCII brackets)
// better than the Chinese models; lines with CJK always keep the first reading.
import Foundation
import ImageIO
import Vision

let data = FileHandle.standardInput.readDataToEndOfFile()
guard let source = CGImageSourceCreateWithData(data as CFData, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
    FileHandle.standardError.write("讀取螢幕的影像無效。\n".data(using: .utf8)!); exit(2)
}
func recognize(_ languages: [String]) throws -> [VNRecognizedTextObservation] {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.recognitionLanguages = languages
    request.usesLanguageCorrection = true
    try VNImageRequestHandler(cgImage: image).perform([request])
    return request.results ?? []
}
func hasCJK(_ text: String) -> Bool {
    text.unicodeScalars.contains { (0x2E80...0x9FFF).contains($0.value) || (0xAC00...0xD7AF).contains($0.value)
        || (0xF900...0xFAFF).contains($0.value) || (0x20000...0x3FFFF).contains($0.value) }
}
// Intersection over union of two normalized boxes. Only a box covering the same text as the line qualifies, so an
// English pass that splits a line (truncation) or merges two lines (duplication) never replaces it.
func overlap(_ a: CGRect, _ b: CGRect) -> Double {
    let shared = a.intersection(b); if shared.isNull { return 0 }
    let common = Double(shared.width * shared.height), union = Double(a.width * a.height + b.width * b.height) - common
    return union > 0 ? common / union : 0
}
let lines: [VNRecognizedTextObservation]
do { lines = try recognize(["zh-Hant", "zh-Hans", "en-US"]) }
catch {
    // The original language set, should a Vision revision refuse the Traditional combination.
    do { lines = try recognize(["zh-Hans", "en-US"]) }
    catch { FileHandle.standardError.write("\(error.localizedDescription)\n".data(using: .utf8)!); exit(3) }
}
func best(_ observation: VNRecognizedTextObservation) -> VNRecognizedText? { observation.topCandidates(1).first }
let latin = lines.contains { !hasCJK(best($0)?.string ?? "") } ? ((try? recognize(["en-US"])) ?? []) : []
let width = Double(image.width), height = Double(image.height)
var output = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
for (index, observation) in lines.enumerated() {
    guard var candidate = best(observation) else { continue }
    if !hasCJK(candidate.string),
       let match = latin.map({ (line: $0, score: overlap($0.boundingBox, observation.boundingBox)) }).filter({ $0.score > 0.5 }).max(by: { $0.score < $1.score }),
       let english = best(match.line), !english.string.trimmingCharacters(in: .whitespaces).isEmpty, !hasCJK(english.string) {
        candidate = english
    }
    let box = observation.boundingBox // normalized, bottom-left origin
    let text = candidate.string.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " ")
    output += "5\t1\t1\t1\t\(index + 1)\t1\t\(Int(box.minX * width))\t\(Int((1 - box.maxY) * height))\t\(Int(box.width * width))\t\(Int(box.height * height))\t\(Int(candidate.confidence * 100))\t\(text)\n"
}
FileHandle.standardOutput.write(output.data(using: .utf8)!)
