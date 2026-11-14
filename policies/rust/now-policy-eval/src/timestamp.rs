//! Timestamps ([§timestamps]).
//!
//! [§timestamps]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#timestamps

use core::fmt;
use core::str::FromStr;

/// An instant, read from a contract timestamp such as `2026-01-31T12:00:00Z` ([§timestamps]).
///
/// Timestamps compare as instants, after applying each one's offset, at nanosecond precision.
///
/// [§timestamps]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#timestamps
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct Timestamp {
    /// Seconds since 0001-01-01T00:00:00Z, negative for an instant before it that an offset reaches.
    seconds: i64,
    /// INVARIANT: `nanoseconds < 1_000_000_000`.
    nanoseconds: u32,
}

/// The text is not a contract timestamp.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct InvalidTimestamp;

impl fmt::Display for InvalidTimestamp {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("invalid timestamp")
    }
}

impl core::error::Error for InvalidTimestamp {}

impl FromStr for Timestamp {
    type Err = InvalidTimestamp;

    fn from_str(text: &str) -> Result<Self, Self::Err> {
        let mut cursor = Cursor {
            bytes: text.as_bytes(),
            pos: 0,
        };

        let year = cursor.digits(4)?;
        cursor.expect(b'-')?;
        let month = cursor.digits(2)?;
        cursor.expect(b'-')?;
        let day = cursor.digits(2)?;
        cursor.expect(b'T')?;
        let hour = cursor.digits(2)?;
        cursor.expect(b':')?;
        let minute = cursor.digits(2)?;
        cursor.expect(b':')?;
        let second = cursor.digits(2)?;

        let mut nanoseconds: u32 = 0;
        if cursor.peek() == Some(b'.') {
            cursor.pos += 1;
            let mut count = 0;
            while let Some(digit @ b'0'..=b'9') = cursor.peek() {
                if !(count < 9) {
                    return Err(InvalidTimestamp);
                }
                nanoseconds = nanoseconds * 10 + u32::from(digit - b'0');
                count += 1;
                cursor.pos += 1;
            }
            if count == 0 {
                return Err(InvalidTimestamp);
            }
            nanoseconds *= 10u32.pow(9 - count);
        }

        let offset_minutes: i64 = match cursor.peek() {
            Some(b'Z') => {
                cursor.pos += 1;
                0
            }
            Some(sign @ (b'+' | b'-')) => {
                cursor.pos += 1;
                let hours = cursor.digits(2)?;
                cursor.expect(b':')?;
                let minutes = cursor.digits(2)?;
                if !(hours <= 23 && minutes <= 59) {
                    return Err(InvalidTimestamp);
                }
                let magnitude = i64::from(hours * 60 + minutes);
                if sign == b'+' { magnitude } else { -magnitude }
            }
            _ => return Err(InvalidTimestamp),
        };

        if cursor.pos != text.len() {
            return Err(InvalidTimestamp);
        }

        if !((1..=9999).contains(&year)
            && (1..=12).contains(&month)
            && 1 <= day
            && day <= days_in_month(year, month)
            && hour <= 23
            && minute <= 59
            && second <= 59)
        {
            return Err(InvalidTimestamp);
        }

        let days = days_since_epoch(year, month, day);
        let local_seconds = days * 86_400 + i64::from(hour * 3600 + minute * 60 + second);

        Ok(Self {
            seconds: local_seconds - offset_minutes * 60,
            nanoseconds,
        })
    }
}

struct Cursor<'a> {
    bytes: &'a [u8],
    pos: usize,
}

impl Cursor<'_> {
    fn peek(&self) -> Option<u8> {
        self.bytes.get(self.pos).copied()
    }

    fn expect(&mut self, byte: u8) -> Result<(), InvalidTimestamp> {
        if self.peek() == Some(byte) {
            self.pos += 1;
            Ok(())
        } else {
            Err(InvalidTimestamp)
        }
    }

    /// Reads exactly `count` ASCII digits as a number.
    fn digits(&mut self, count: usize) -> Result<u32, InvalidTimestamp> {
        let digits = self.bytes.get(self.pos..self.pos + count).ok_or(InvalidTimestamp)?;
        let mut value = 0;
        for digit in digits {
            if !digit.is_ascii_digit() {
                return Err(InvalidTimestamp);
            }
            value = value * 10 + u32::from(digit - b'0');
        }
        self.pos += count;
        Ok(value)
    }
}

fn is_leap_year(year: u32) -> bool {
    (year.is_multiple_of(4) && !year.is_multiple_of(100)) || year.is_multiple_of(400)
}

fn days_in_month(year: u32, month: u32) -> u32 {
    match month {
        2 if is_leap_year(year) => 29,
        2 => 28,
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

/// Returns the days from 0001-01-01 to the given date of the proleptic Gregorian calendar.
fn days_since_epoch(year: u32, month: u32, day: u32) -> i64 {
    let years_before = i64::from(year - 1);
    let mut days = years_before * 365 + years_before / 4 - years_before / 100 + years_before / 400;
    for earlier_month in 1..month {
        days += i64::from(days_in_month(year, earlier_month));
    }
    days + i64::from(day - 1)
}
