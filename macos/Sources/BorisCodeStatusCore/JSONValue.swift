import Foundation

/// A JSON tree that keeps object members in their original order and numbers in their original
/// spelling.
///
/// It exists for `~/.claude/settings.json`. That file holds unrelated user configuration and is
/// read-modify-written, never regenerated; `JSONSerialization` would round-trip it through
/// unordered dictionaries and reformatted numbers, so a merge that changed one entry would
/// reshuffle the whole file. It is also how `state.json` is written, so the member order matches
/// the Windows build's.
public indirect enum JSONValue: Equatable, Sendable {
    case null
    case bool(Bool)
    /// The literal as written, so `1.0` stays `1.0` and large integers lose no precision.
    case number(String)
    case string(String)
    case array([JSONValue])
    case object([(String, JSONValue)])

    public static func == (lhs: JSONValue, rhs: JSONValue) -> Bool {
        switch (lhs, rhs) {
        case (.null, .null): true
        case let (.bool(a), .bool(b)): a == b
        case let (.number(a), .number(b)): a == b
        case let (.string(a), .string(b)): a == b
        case let (.array(a), .array(b)): a == b
        case let (.object(a), .object(b)): a.count == b.count && zip(a, b).allSatisfy { $0.0 == $1.0 && $0.1 == $1.1 }
        default: false
        }
    }

    public static func number(_ value: Double) -> JSONValue {
        if value.rounded() == value, abs(value) < 1e15 {
            return .number(String(Int64(value)))
        }
        return .number(String(value))
    }

    public static func number(_ value: Int64) -> JSONValue { .number(String(value)) }

    public static func string(_ value: String?) -> JSONValue { value.map { .string($0) } ?? .null }

    public var stringValue: String? {
        if case let .string(value) = self { return value }
        return nil
    }

    public var arrayValue: [JSONValue]? {
        if case let .array(value) = self { return value }
        return nil
    }

    public var objectValue: [(String, JSONValue)]? {
        if case let .object(value) = self { return value }
        return nil
    }

    /// The member named `key`, if this is an object that has one.
    public subscript(key: String) -> JSONValue? {
        objectValue?.first { $0.0 == key }?.1
    }

    /// Replaces the member named `key` in place, or appends it, keeping every other member where
    /// it was. Does nothing unless this is an object.
    public mutating func set(_ key: String, _ value: JSONValue) {
        guard case var .object(members) = self else { return }
        if let index = members.firstIndex(where: { $0.0 == key }) {
            members[index].1 = value
        } else {
            members.append((key, value))
        }
        self = .object(members)
    }

    /// Removes the member named `key`. Does nothing unless this is an object.
    public mutating func remove(_ key: String) {
        guard case var .object(members) = self else { return }
        members.removeAll { $0.0 == key }
        self = .object(members)
    }
}

// MARK: - Writing

extension JSONValue {
    /// Indented with two spaces, as the Windows build writes both files.
    public func serialized() -> String {
        var out = ""
        write(to: &out, indent: 0)
        return out
    }

    private func write(to out: inout String, indent: Int) {
        switch self {
        case .null:
            out += "null"
        case let .bool(value):
            out += value ? "true" : "false"
        case let .number(literal):
            out += literal
        case let .string(value):
            JSONValue.writeString(value, to: &out)
        case let .array(items):
            if items.isEmpty {
                out += "[]"
                return
            }
            out += "[\n"
            for (index, item) in items.enumerated() {
                out += String(repeating: "  ", count: indent + 1)
                item.write(to: &out, indent: indent + 1)
                out += index < items.count - 1 ? ",\n" : "\n"
            }
            out += String(repeating: "  ", count: indent) + "]"
        case let .object(members):
            if members.isEmpty {
                out += "{}"
                return
            }
            out += "{\n"
            for (index, (key, value)) in members.enumerated() {
                out += String(repeating: "  ", count: indent + 1)
                JSONValue.writeString(key, to: &out)
                out += ": "
                value.write(to: &out, indent: indent + 1)
                out += index < members.count - 1 ? ",\n" : "\n"
            }
            out += String(repeating: "  ", count: indent) + "}"
        }
    }

    private static func writeString(_ value: String, to out: inout String) {
        out += "\""
        for scalar in value.unicodeScalars {
            switch scalar {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            case "\u{08}": out += "\\b"
            case "\u{0C}": out += "\\f"
            case _ where scalar.value < 0x20:
                out += String(format: "\\u%04x", scalar.value)
            default:
                out.unicodeScalars.append(scalar)
            }
        }
        out += "\""
    }
}

// MARK: - Parsing

extension JSONValue {
    public struct ParseError: Error, CustomStringConvertible {
        public let description: String
    }

    /// Parses a complete JSON document. Strict: trailing content or a malformed literal is an
    /// error, because the caller's answer to an error is to leave the file alone.
    public static func parse(_ text: String) throws -> JSONValue {
        var parser = Parser(scalars: Array(text.unicodeScalars))
        parser.skipWhitespace()
        let value = try parser.parseValue(depth: 0)
        parser.skipWhitespace()
        guard parser.atEnd else { throw parser.error("unexpected content after the document") }
        return value
    }

    private struct Parser {
        let scalars: [Unicode.Scalar]
        var position = 0

        /// Deeper than any real settings file; stops a hostile one overflowing the stack.
        static let maxDepth = 512

        var atEnd: Bool { position >= scalars.count }

        func error(_ message: String) -> ParseError {
            ParseError(description: "\(message) at offset \(position)")
        }

        mutating func skipWhitespace() {
            while !atEnd, [" ", "\n", "\r", "\t"].contains(scalars[position]) {
                position += 1
            }
        }

        mutating func expect(_ literal: String) throws {
            for scalar in literal.unicodeScalars {
                guard !atEnd, scalars[position] == scalar else { throw error("expected '\(literal)'") }
                position += 1
            }
        }

        mutating func parseValue(depth: Int) throws -> JSONValue {
            guard depth < Parser.maxDepth else { throw error("nested too deeply") }
            guard !atEnd else { throw error("unexpected end of input") }

            switch scalars[position] {
            case "{": return try parseObject(depth: depth)
            case "[": return try parseArray(depth: depth)
            case "\"": return .string(try parseString())
            case "t": try expect("true"); return .bool(true)
            case "f": try expect("false"); return .bool(false)
            case "n": try expect("null"); return .null
            default: return .number(try parseNumber())
            }
        }

        mutating func parseObject(depth: Int) throws -> JSONValue {
            position += 1
            var members: [(String, JSONValue)] = []
            skipWhitespace()
            if !atEnd, scalars[position] == "}" {
                position += 1
                return .object(members)
            }
            while true {
                skipWhitespace()
                guard !atEnd, scalars[position] == "\"" else { throw error("expected a member name") }
                let key = try parseString()
                skipWhitespace()
                try expect(":")
                skipWhitespace()
                members.append((key, try parseValue(depth: depth + 1)))
                skipWhitespace()
                guard !atEnd else { throw error("unterminated object") }
                if scalars[position] == "," {
                    position += 1
                    continue
                }
                try expect("}")
                return .object(members)
            }
        }

        mutating func parseArray(depth: Int) throws -> JSONValue {
            position += 1
            var items: [JSONValue] = []
            skipWhitespace()
            if !atEnd, scalars[position] == "]" {
                position += 1
                return .array(items)
            }
            while true {
                skipWhitespace()
                items.append(try parseValue(depth: depth + 1))
                skipWhitespace()
                guard !atEnd else { throw error("unterminated array") }
                if scalars[position] == "," {
                    position += 1
                    continue
                }
                try expect("]")
                return .array(items)
            }
        }

        mutating func parseString() throws -> String {
            position += 1
            var result = String.UnicodeScalarView()
            while true {
                guard !atEnd else { throw error("unterminated string") }
                let scalar = scalars[position]
                position += 1
                switch scalar {
                case "\"":
                    return String(result)
                case "\\":
                    guard !atEnd else { throw error("unterminated escape") }
                    let escaped = scalars[position]
                    position += 1
                    switch escaped {
                    case "\"": result.append("\"")
                    case "\\": result.append("\\")
                    case "/": result.append("/")
                    case "b": result.append("\u{08}")
                    case "f": result.append("\u{0C}")
                    case "n": result.append("\n")
                    case "r": result.append("\r")
                    case "t": result.append("\t")
                    case "u": result.append(try parseUnicodeEscape())
                    default: throw error("invalid escape")
                    }
                default:
                    guard scalar.value >= 0x20 else { throw error("control character in string") }
                    result.append(scalar)
                }
            }
        }

        mutating func parseHex4() throws -> UInt32 {
            guard position + 4 <= scalars.count else { throw error("short \\u escape") }
            let hex = String(String.UnicodeScalarView(scalars[position..<position + 4]))
            guard let value = UInt32(hex, radix: 16) else { throw error("invalid \\u escape") }
            position += 4
            return value
        }

        mutating func parseUnicodeEscape() throws -> Unicode.Scalar {
            let high = try parseHex4()
            if (0xD800...0xDBFF).contains(high) {
                // A surrogate pair arrives as two escapes; anything else is not valid text.
                try expect("\\u")
                let low = try parseHex4()
                guard (0xDC00...0xDFFF).contains(low),
                      let scalar = Unicode.Scalar(0x10000 + ((high - 0xD800) << 10) + (low - 0xDC00))
                else { throw error("invalid surrogate pair") }
                return scalar
            }
            guard let scalar = Unicode.Scalar(high) else { throw error("invalid \\u escape") }
            return scalar
        }

        mutating func parseNumber() throws -> String {
            let start = position
            func digits() -> Int {
                let begin = position
                while !atEnd, ("0"..."9").contains(scalars[position]) { position += 1 }
                return position - begin
            }

            if !atEnd, scalars[position] == "-" { position += 1 }
            guard digits() > 0 else { throw error("invalid value") }
            if !atEnd, scalars[position] == "." {
                position += 1
                guard digits() > 0 else { throw error("invalid number") }
            }
            if !atEnd, scalars[position] == "e" || scalars[position] == "E" {
                position += 1
                if !atEnd, scalars[position] == "+" || scalars[position] == "-" { position += 1 }
                guard digits() > 0 else { throw error("invalid number") }
            }
            return String(String.UnicodeScalarView(scalars[start..<position]))
        }
    }
}
