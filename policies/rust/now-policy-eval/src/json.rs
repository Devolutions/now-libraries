//! A strict RFC 8259 reader that keeps what the contract needs and general parsers hide ([§json]).
//!
//! It reports duplicate keys after unescaping with their pointers, keeps the raw spelling of numbers, rejects unpaired surrogates and limits nesting depth.
//!
//! [§json]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#json

/// The deepest nesting of arrays and objects a document may have, counting the top-level object as level 1.
pub(crate) const MAX_DEPTH: usize = 64;

/// A JSON value.
#[derive(Debug, Clone, PartialEq)]
pub(crate) enum Value {
    Null,
    Bool(bool),
    /// A number, as it is spelled in the document.
    Number(String),
    String(String),
    Array(Vec<Value>),
    /// The members of an object in document order, repeated keys included.
    Object(Vec<(String, Value)>),
}

/// A well-formed document.
#[derive(Debug)]
pub(crate) struct Document {
    pub(crate) value: Value,
    /// The pointer of every member whose key an earlier member of the same object already has.
    pub(crate) duplicate_keys: Vec<String>,
}

/// The document is not well-formed JSON within the contract's limits.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Malformed;

/// Reads `text`, which has no byte order mark, as one JSON value.
pub(crate) fn parse(text: &str) -> Result<Document, Malformed> {
    let mut parser = Parser {
        bytes: text.as_bytes(),
        text,
        pos: 0,
        path: Vec::new(),
        duplicate_keys: Vec::new(),
    };

    parser.skip_whitespace();
    let value = parser.parse_value(1)?;
    parser.skip_whitespace();

    if parser.pos != parser.bytes.len() {
        return Err(Malformed);
    }

    Ok(Document {
        value,
        duplicate_keys: parser.duplicate_keys,
    })
}

/// Appends the RFC 6901 reference token for `key` to `pointer`.
pub(crate) fn push_token(pointer: &mut String, key: &str) {
    pointer.push('/');
    for c in key.chars() {
        match c {
            '~' => pointer.push_str("~0"),
            '/' => pointer.push_str("~1"),
            c => pointer.push(c),
        }
    }
}

struct Parser<'a> {
    bytes: &'a [u8],
    text: &'a str,
    pos: usize,
    /// The reference tokens of the value being read.
    path: Vec<String>,
    duplicate_keys: Vec<String>,
}

impl Parser<'_> {
    fn peek(&self) -> Option<u8> {
        self.bytes.get(self.pos).copied()
    }

    fn skip_whitespace(&mut self) {
        while let Some(b' ' | b'\t' | b'\n' | b'\r') = self.peek() {
            self.pos += 1;
        }
    }

    fn expect_literal(&mut self, literal: &str) -> Result<(), Malformed> {
        if self.bytes[self.pos..].starts_with(literal.as_bytes()) {
            self.pos += literal.len();
            Ok(())
        } else {
            Err(Malformed)
        }
    }

    /// Reads one value at nesting level `depth`, where an array or object at that level counts as level `depth`.
    fn parse_value(&mut self, depth: usize) -> Result<Value, Malformed> {
        match self.peek().ok_or(Malformed)? {
            b'{' => self.parse_object(depth),
            b'[' => self.parse_array(depth),
            b'"' => self.parse_string().map(Value::String),
            b't' => self.expect_literal("true").map(|()| Value::Bool(true)),
            b'f' => self.expect_literal("false").map(|()| Value::Bool(false)),
            b'n' => self.expect_literal("null").map(|()| Value::Null),
            b'-' | b'0'..=b'9' => self.parse_number(),
            _ => Err(Malformed),
        }
    }

    fn parse_object(&mut self, depth: usize) -> Result<Value, Malformed> {
        if !(depth <= MAX_DEPTH) {
            return Err(Malformed);
        }

        self.pos += 1; // {
        let mut members: Vec<(String, Value)> = Vec::new();
        let mut keys = std::collections::HashSet::new();

        self.skip_whitespace();
        if self.peek() == Some(b'}') {
            self.pos += 1;
            return Ok(Value::Object(members));
        }

        loop {
            self.skip_whitespace();
            if self.peek() != Some(b'"') {
                return Err(Malformed);
            }
            let key = self.parse_string()?;

            self.skip_whitespace();
            if self.peek() != Some(b':') {
                return Err(Malformed);
            }
            self.pos += 1;
            self.skip_whitespace();

            if !keys.insert(key.clone()) {
                let mut pointer = String::new();
                for token in self.path.iter().chain(core::iter::once(&key)) {
                    push_token(&mut pointer, token);
                }
                self.duplicate_keys.push(pointer);
            }

            self.path.push(key.clone());
            let value = self.parse_value(depth + 1);
            self.path.pop();
            members.push((key, value?));

            self.skip_whitespace();
            match self.peek() {
                Some(b',') => self.pos += 1,
                Some(b'}') => {
                    self.pos += 1;
                    return Ok(Value::Object(members));
                }
                _ => return Err(Malformed),
            }
        }
    }

    fn parse_array(&mut self, depth: usize) -> Result<Value, Malformed> {
        if !(depth <= MAX_DEPTH) {
            return Err(Malformed);
        }

        self.pos += 1; // [
        let mut elements = Vec::new();

        self.skip_whitespace();
        if self.peek() == Some(b']') {
            self.pos += 1;
            return Ok(Value::Array(elements));
        }

        loop {
            self.skip_whitespace();
            self.path.push(elements.len().to_string());
            let value = self.parse_value(depth + 1);
            self.path.pop();
            elements.push(value?);

            self.skip_whitespace();
            match self.peek() {
                Some(b',') => self.pos += 1,
                Some(b']') => {
                    self.pos += 1;
                    return Ok(Value::Array(elements));
                }
                _ => return Err(Malformed),
            }
        }
    }

    /// Parses a number and keeps its spelling.
    ///
    /// INVARIANT: a spelling has no leading zero except in `0` itself, so a spelling with more digits is a larger number.
    fn parse_number(&mut self) -> Result<Value, Malformed> {
        let start = self.pos;

        if self.peek() == Some(b'-') {
            self.pos += 1;
        }

        match self.peek() {
            Some(b'0') => self.pos += 1,
            Some(b'1'..=b'9') => self.skip_digits(),
            _ => return Err(Malformed),
        }

        if self.peek() == Some(b'.') {
            self.pos += 1;
            self.expect_digits()?;
        }

        if let Some(b'e' | b'E') = self.peek() {
            self.pos += 1;
            if let Some(b'+' | b'-') = self.peek() {
                self.pos += 1;
            }
            self.expect_digits()?;
        }

        Ok(Value::Number(self.text[start..self.pos].to_owned()))
    }

    fn skip_digits(&mut self) {
        while let Some(b'0'..=b'9') = self.peek() {
            self.pos += 1;
        }
    }

    fn expect_digits(&mut self) -> Result<(), Malformed> {
        let start = self.pos;
        self.skip_digits();
        if self.pos == start { Err(Malformed) } else { Ok(()) }
    }

    fn parse_string(&mut self) -> Result<String, Malformed> {
        self.pos += 1; // "
        let mut out = String::new();

        loop {
            let run_start = self.pos;
            while let Some(byte) = self.peek() {
                if byte == b'"' || byte == b'\\' || byte < 0x20 {
                    break;
                }
                self.pos += 1;
            }
            // The run ends at an ASCII byte or at the end of the text, so it is a whole number of UTF-8 sequences.
            out.push_str(&self.text[run_start..self.pos]);

            match self.peek().ok_or(Malformed)? {
                b'"' => {
                    self.pos += 1;
                    return Ok(out);
                }
                b'\\' => {
                    self.pos += 1;
                    let escape = self.peek().ok_or(Malformed)?;
                    self.pos += 1;
                    match escape {
                        b'"' => out.push('"'),
                        b'\\' => out.push('\\'),
                        b'/' => out.push('/'),
                        b'b' => out.push('\u{8}'),
                        b'f' => out.push('\u{c}'),
                        b'n' => out.push('\n'),
                        b'r' => out.push('\r'),
                        b't' => out.push('\t'),
                        b'u' => out.push(self.parse_unicode_escape()?),
                        _ => return Err(Malformed),
                    }
                }
                // A control character must be escaped.
                _ => return Err(Malformed),
            }
        }
    }

    /// Reads the code point of a `\u` escape whose `\u` was just read, pairing surrogates.
    fn parse_unicode_escape(&mut self) -> Result<char, Malformed> {
        let first = self.parse_hex4()?;

        match first {
            0xD800..=0xDBFF => {
                if !self.bytes[self.pos..].starts_with(b"\\u") {
                    return Err(Malformed);
                }
                self.pos += 2;
                let second = self.parse_hex4()?;
                if !(0xDC00..=0xDFFF).contains(&second) {
                    return Err(Malformed);
                }
                let code_point = 0x10000 + ((u32::from(first) - 0xD800) << 10) + (u32::from(second) - 0xDC00);
                char::from_u32(code_point).ok_or(Malformed)
            }
            0xDC00..=0xDFFF => Err(Malformed),
            _ => char::from_u32(u32::from(first)).ok_or(Malformed),
        }
    }

    fn parse_hex4(&mut self) -> Result<u16, Malformed> {
        let digits = self.bytes.get(self.pos..self.pos + 4).ok_or(Malformed)?;
        let mut value: u16 = 0;
        for digit in digits {
            let nibble = match digit {
                b'0'..=b'9' => digit - b'0',
                b'a'..=b'f' => digit - b'a' + 10,
                b'A'..=b'F' => digit - b'A' + 10,
                _ => return Err(Malformed),
            };
            value = (value << 4) | u16::from(nibble);
        }
        self.pos += 4;
        Ok(value)
    }
}
