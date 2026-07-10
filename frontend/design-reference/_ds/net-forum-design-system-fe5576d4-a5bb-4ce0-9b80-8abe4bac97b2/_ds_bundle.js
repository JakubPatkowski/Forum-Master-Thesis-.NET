/* @ds-bundle: {"format":4,"namespace":"NETForumDesignSystem_fe5576","components":[{"name":"Badge","sourcePath":"components/Badge.jsx"},{"name":"Button","sourcePath":"components/Button.jsx"},{"name":"Card","sourcePath":"components/Card.jsx"},{"name":"Input","sourcePath":"components/Input.jsx"},{"name":"ReactionButton","sourcePath":"components/ReactionButton.jsx"},{"name":"Tag","sourcePath":"components/Tag.jsx"},{"name":"Toast","sourcePath":"components/Toast.jsx"}],"sourceHashes":{"components/Badge.jsx":"310a3befcb70","components/Button.jsx":"48ca0f4e5b8d","components/Card.jsx":"41b7b774a82b","components/Input.jsx":"340cea7917d9","components/ReactionButton.jsx":"3b9a9f5b79ae","components/Tag.jsx":"905afd2e648a","components/Toast.jsx":"9793de44d407"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.NETForumDesignSystem_fe5576 = window.NETForumDesignSystem_fe5576 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/Badge.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Badge component — role, visibility, status badges
 * Compact pill-shaped indicator with optional icon
 */
function Badge({
  variant = 'default',
  children,
  icon,
  ...props
}) {
  const variantStyles = {
    default: {
      backgroundColor: 'var(--color-surface-tertiary)',
      color: 'var(--color-text-primary)'
    },
    accent: {
      backgroundColor: 'var(--color-accent-subtle)',
      color: 'var(--color-accent-base)'
    },
    success: {
      backgroundColor: 'rgba(16, 185, 129, 0.15)',
      color: 'var(--color-success-light)'
    },
    warning: {
      backgroundColor: 'rgba(245, 158, 11, 0.15)',
      color: 'var(--color-warning-light)'
    },
    error: {
      backgroundColor: 'rgba(239, 68, 68, 0.15)',
      color: 'var(--color-error-light)'
    }
  };
  const style = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 'var(--space-1)',
    padding: 'var(--space-1) var(--space-2)',
    borderRadius: '9999px',
    fontSize: 'var(--font-size-xs)',
    fontWeight: 'var(--font-weight-medium)',
    whiteSpace: 'nowrap',
    ...variantStyles[variant]
  };
  return /*#__PURE__*/React.createElement("span", _extends({
    style: style
  }, props), icon && /*#__PURE__*/React.createElement("span", null, icon), children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Badge.jsx", error: String((e && e.message) || e) }); }

// components/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Button component — primary, secondary, ghost, danger variants
 * All states: default, hover, focus, active, disabled, loading
 */
function Button({
  variant = 'primary',
  size = 'md',
  disabled = false,
  loading = false,
  children,
  onClick,
  ...props
}) {
  const variantStyles = {
    primary: {
      backgroundColor: 'var(--color-accent-base)',
      color: 'var(--color-text-inverse)'
    },
    secondary: {
      backgroundColor: 'var(--color-surface-secondary)',
      color: 'var(--color-text-primary)',
      border: '1px solid var(--color-border-default)'
    },
    ghost: {
      backgroundColor: 'transparent',
      color: 'var(--color-accent-base)'
    },
    danger: {
      backgroundColor: 'var(--color-error)',
      color: 'var(--color-text-inverse)'
    }
  };
  const sizeStyles = {
    sm: {
      fontSize: 'var(--font-size-sm)',
      padding: 'var(--space-2) var(--space-3)',
      borderRadius: 'var(--radius-sm)',
      minHeight: '32px'
    },
    md: {
      fontSize: 'var(--font-size-body)',
      padding: 'var(--space-3) var(--space-6)',
      borderRadius: 'var(--radius-md)',
      minHeight: '40px'
    },
    lg: {
      fontSize: 'var(--font-size-body)',
      padding: 'var(--space-4) var(--space-8)',
      borderRadius: 'var(--radius-md)',
      minHeight: '48px'
    }
  };
  const baseStyle = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 'var(--font-weight-semibold)',
    fontFamily: 'var(--font-sans)',
    cursor: disabled ? 'not-allowed' : 'pointer',
    transition: 'all 0.15s ease',
    border: 'none',
    opacity: disabled || loading ? 0.6 : 1,
    ...sizeStyles[size],
    ...variantStyles[variant]
  };
  return /*#__PURE__*/React.createElement("button", _extends({
    style: baseStyle,
    disabled: disabled || loading,
    onClick: onClick
  }, props), loading ? '⟳ ' : '', children);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Button.jsx", error: String((e && e.message) || e) }); }

// components/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Card component — generic container for content, threads, comments, categories
 * Used as the base for ThreadCard, CommentNode, CategoryCard, UserCard
 */
function Card({
  children,
  onClick,
  hoverable = false,
  style = {},
  ...props
}) {
  const baseStyle = {
    backgroundColor: 'var(--color-surface-secondary)',
    border: '1px solid var(--color-border-default)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-4)',
    transition: 'all 0.15s ease',
    cursor: hoverable ? 'pointer' : 'default',
    ...style
  };
  const onMouseEnter = hoverable ? e => {
    e.currentTarget.style.backgroundColor = 'var(--color-surface-tertiary)';
    e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
  } : null;
  const onMouseLeave = hoverable ? e => {
    e.currentTarget.style.backgroundColor = 'var(--color-surface-secondary)';
    e.currentTarget.style.boxShadow = 'none';
  } : null;
  return /*#__PURE__*/React.createElement("div", _extends({
    style: baseStyle,
    onClick: onClick,
    onMouseEnter: onMouseEnter,
    onMouseLeave: onMouseLeave
  }, props), children);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Card.jsx", error: String((e && e.message) || e) }); }

// components/Input.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Input component — text, email, password, number
 * Supports label, error state, disabled, placeholder
 */
function Input({
  type = 'text',
  label,
  error,
  disabled = false,
  placeholder,
  value,
  onChange,
  ...props
}) {
  const inputStyle = {
    display: 'block',
    width: '100%',
    padding: 'var(--space-3) var(--space-4)',
    fontSize: 'var(--font-size-body)',
    fontFamily: 'var(--font-sans)',
    backgroundColor: 'var(--color-surface-secondary)',
    color: 'var(--color-text-primary)',
    border: error ? '1px solid var(--color-error)' : '1px solid var(--color-border-default)',
    borderRadius: 'var(--radius-md)',
    transition: 'all 0.15s ease',
    opacity: disabled ? 0.6 : 1
  };
  const labelStyle = {
    display: 'block',
    marginBottom: 'var(--space-2)',
    fontSize: 'var(--font-size-sm)',
    fontWeight: 'var(--font-weight-semibold)',
    color: 'var(--color-text-primary)'
  };
  const errorStyle = {
    marginTop: 'var(--space-2)',
    fontSize: 'var(--font-size-sm)',
    color: 'var(--color-error)'
  };
  const containerStyle = {
    display: 'flex',
    flexDirection: 'column'
  };
  return /*#__PURE__*/React.createElement("div", {
    style: containerStyle
  }, label && /*#__PURE__*/React.createElement("label", {
    style: labelStyle
  }, label), /*#__PURE__*/React.createElement("input", _extends({
    type: type,
    style: inputStyle,
    placeholder: placeholder,
    disabled: disabled,
    value: value,
    onChange: onChange
  }, props)), error && /*#__PURE__*/React.createElement("div", {
    style: errorStyle
  }, error));
}
Object.assign(__ds_scope, { Input });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Input.jsx", error: String((e && e.message) || e) }); }

// components/ReactionButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ReactionButton component — like button with count, idempotent toggle
 */
function ReactionButton({
  count = 0,
  reacted = false,
  onToggle,
  disabled = false,
  ...props
}) {
  const style = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 'var(--space-2)',
    padding: 'var(--space-2) var(--space-3)',
    backgroundColor: reacted ? 'var(--color-accent-subtle)' : 'transparent',
    color: reacted ? 'var(--color-accent-base)' : 'var(--color-text-secondary)',
    border: '1px solid var(--color-border-default)',
    borderRadius: 'var(--radius-sm)',
    fontSize: 'var(--font-size-sm)',
    fontWeight: 'var(--font-weight-medium)',
    cursor: disabled ? 'not-allowed' : 'pointer',
    transition: 'all 0.15s ease',
    fontFamily: 'var(--font-sans)'
  };
  return /*#__PURE__*/React.createElement("button", _extends({
    style: style,
    onClick: onToggle,
    disabled: disabled
  }, props), /*#__PURE__*/React.createElement("span", null, "\uD83D\uDC4D"), /*#__PURE__*/React.createElement("span", null, count));
}
Object.assign(__ds_scope, { ReactionButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/ReactionButton.jsx", error: String((e && e.message) || e) }); }

// components/Tag.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Tag component — clickable tag chip for thread tags, category filters
 * Removable variant for tag input
 */
function Tag({
  children,
  onClick,
  onRemove,
  removable = false,
  ...props
}) {
  const style = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 'var(--space-2)',
    padding: 'var(--space-1) var(--space-3)',
    backgroundColor: 'var(--color-neutral-700)',
    color: 'var(--color-text-primary)',
    borderRadius: 'var(--radius-sm)',
    fontSize: 'var(--font-size-sm)',
    cursor: onClick ? 'pointer' : 'default',
    transition: 'all 0.15s ease',
    border: 'none'
  };
  if (removable) {
    return /*#__PURE__*/React.createElement("div", _extends({
      style: style
    }, props), /*#__PURE__*/React.createElement("span", null, children), /*#__PURE__*/React.createElement("button", {
      onClick: onRemove,
      style: {
        background: 'none',
        border: 'none',
        color: 'inherit',
        cursor: 'pointer',
        padding: 0,
        fontSize: '1.2em'
      }
    }, "\xD7"));
  }
  return /*#__PURE__*/React.createElement("button", _extends({
    style: style,
    onClick: onClick
  }, props), children);
}
Object.assign(__ds_scope, { Tag });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Tag.jsx", error: String((e && e.message) || e) }); }

// components/Toast.jsx
try { (() => {
/**
 * Toast component — ephemeral notification (success, error, warning, info)
 */
function Toast({
  variant = 'success',
  title,
  message,
  onClose,
  autoClose = true,
  duration = 4000
}) {
  React.useEffect(() => {
    if (autoClose) {
      const timer = setTimeout(onClose, duration);
      return () => clearTimeout(timer);
    }
  }, [autoClose, duration, onClose]);
  const variantStyles = {
    success: {
      backgroundColor: 'rgba(16, 185, 129, 0.15)',
      borderColor: 'var(--color-success)',
      color: 'var(--color-success-light)'
    },
    error: {
      backgroundColor: 'rgba(239, 68, 68, 0.15)',
      borderColor: 'var(--color-error)',
      color: 'var(--color-error-light)'
    },
    warning: {
      backgroundColor: 'rgba(245, 158, 11, 0.15)',
      borderColor: 'var(--color-warning)',
      color: 'var(--color-warning-light)'
    },
    info: {
      backgroundColor: 'rgba(59, 130, 246, 0.15)',
      borderColor: 'var(--color-info)',
      color: 'var(--color-info-light)'
    }
  };
  const style = {
    position: 'fixed',
    bottom: 'var(--space-4)',
    right: 'var(--space-4)',
    padding: 'var(--space-4)',
    borderRadius: 'var(--radius-md)',
    border: '1px solid',
    maxWidth: '400px',
    zIndex: 9999,
    ...variantStyles[variant]
  };
  return /*#__PURE__*/React.createElement("div", {
    style: style
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontWeight: 'var(--font-weight-semibold)'
    }
  }, title), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 'var(--font-size-sm)',
      marginTop: 'var(--space-2)'
    }
  }, message));
}
Object.assign(__ds_scope, { Toast });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/Toast.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.Input = __ds_scope.Input;

__ds_ns.ReactionButton = __ds_scope.ReactionButton;

__ds_ns.Tag = __ds_scope.Tag;

__ds_ns.Toast = __ds_scope.Toast;

})();
