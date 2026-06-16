/// Creates a `PduError` with `UnsupportedValue` kind
macro_rules! unsupported_message_err {
    ( $name:expr, class: $class:expr, kind: $kind:expr $(,)? ) => {{
        ironrdp_core::unsupported_value_err(
            "NOW-PROTO",
            $name,
            alloc::format!("CLASS({}); KIND({})", $class, $kind)
        )
    }};
    ( class: $class:expr, kind: $kind:expr $(,)? ) => {{
        unsupported_message_err!(Self::NAME, class: $class, kind: $kind)
    }};
}

/// Ensures that accumulated message size does not overflow u32 message size field.
macro_rules! ensure_now_message_size {
    ($e:expr) => {
        u32::try_from($e).map_err(|_| ironrdp_core::invalid_field_err!("size", "message size overflow"))?;
    };
    ($e1:expr, $e2:expr) => {
        $e1.checked_add($e2)
            .ok_or_else(|| ironrdp_core::invalid_field_err!("size", "message size overflow"))?;
    };

    ($e1:expr, $e2:expr, $($er:expr),+) => {
        $e1.checked_add($e2)
            $(.and_then(|size| size.checked_add($er)))*
            .ok_or_else(|| ironrdp_core::invalid_field_err!("size", "message size overflow"))?;
    };
}

/// Asserts that constant expressions evaluate to `true`.
///
/// From <https://docs.rs/static_assertions/1.1.0/src/static_assertions/const_assert.rs.html#51-57>
macro_rules! const_assert {
    ($x:expr $(,)?) => {
        #[allow(unknown_lints, clippy::eq_op)]
        const _: [(); 0 - !{
            const ASSERT: bool = $x;
            ASSERT
        } as usize] = [];
    };
}

/// Implements additional traits for a borrowing PDU and defines a static-bounded owned version.
macro_rules! impl_pdu_borrowing {
    ($pdu_ty:ident $(<$($lt:lifetime),+>)?, $owned_ty:ident) => {
        pub type $owned_ty = $pdu_ty<'static>;

        impl $crate::ironrdp_core::DecodeOwned for $owned_ty {
            fn decode_owned(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
                let pdu = <$pdu_ty $(<$($lt),+>)? as $crate::ironrdp_core::Decode>::decode(src)?;
                Ok($crate::ironrdp_core::IntoOwned::into_owned(pdu))
            }
        }
    };
}
