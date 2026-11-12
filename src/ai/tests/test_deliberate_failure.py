def test_fails_on_purpose() -> None:
    raise AssertionError("CI check for #10")
