#!/usr/bin/perl
# Replaces literal UI text in XAML with {loc:T key} bindings and collects the English table.
#
#   perl tools/extract-strings.pl <out.json> <file.xaml> [more.xaml ...]
#
# Keys are derived from the English text, so the same wording in two files shares one entry.
use strict;
use warnings;

my $out = shift @ARGV or die "usage: extract-strings.pl <out.json> <files...>\n";
my %table;

# Text that must stay verbatim: keyboard hints, symbols, bare numbers, single glyphs.
my @skip_exact = (
    'G', '0', '1', '0 ms', "\x{2715}", "\x{FF0B}", 'Ctrl+N', 'Ctrl + Enter', 'F2', 'OK',
    '</>', 'PMAK-...', 'GetMan',
);
my %skip = map { $_ => 1 } @skip_exact;

sub decode_xml {
    my ($s) = @_;
    $s =~ s/&lt;/</g;
    $s =~ s/&gt;/>/g;
    $s =~ s/&quot;/"/g;
    $s =~ s/&apos;/'/g;
    $s =~ s/&#(\d+);/chr($1)/ge;
    $s =~ s/&amp;/&/g;
    return $s;
}

sub make_key {
    my ($text) = @_;
    my $slug = lc $text;
    $slug =~ s/[^a-z0-9]+/_/g;
    $slug =~ s/^_+|_+$//g;
    $slug = substr($slug, 0, 44);
    $slug =~ s/_+$//;
    $slug = 'text' if $slug eq '';
    if (length($text) > 44) {
        my $sum = 0;
        $sum = ($sum * 31 + ord($_)) % 99991 for split //, $text;
        $slug .= '_' . $sum;
    }
    return "s.$slug";
}

sub should_skip {
    my ($text) = @_;
    return 1 if $skip{$text};
    return 1 if $text =~ /^\s*$/;
    return 1 if $text =~ /^[\d\s.,:%+\-]+$/;
    return 1 if $text =~ /^\p{L}$/;
    return 1 if $text =~ /^Ctrl\+/;
    return 0;
}

sub localize {
    my ($text, $changed_ref) = @_;
    my $key = make_key($text);
    $table{$key} = $text;
    $$changed_ref = 1;
    return $key;
}

my $attrs = qr/(?<![\w.:])(?:Text|Content|Header|ToolTip|AutomationProperties\.Name|materialDesign:HintAssist\.Hint)/;

for my $file (@ARGV) {
    open my $fh, '<:encoding(UTF-8)', $file or die "cannot read $file: $!";
    local $/;
    my $xaml = <$fh>;
    close $fh;

    my $changed = 0;

    # Plain attributes:  Text="Import"
    $xaml =~ s!($attrs)="([^"{][^"]*)"!
        my ($attr, $raw) = ($1, $2);
        my $text = decode_xml($raw);
        should_skip($text)
            ? qq{$attr="$raw"}
            : qq{$attr="} . chr(123) . qq{loc:T } . localize($text, \$changed) . chr(125) . qq{"};
    !ge;

    # Style setters:  <Setter Property="Text" Value="Send" />
    $xaml =~ s!(<Setter\s+Property="(?:Text|Content|Header|ToolTip)"\s+Value=)"([^"{][^"]*)"!
        my ($head, $raw) = ($1, $2);
        my $text = decode_xml($raw);
        should_skip($text)
            ? qq{$head"$raw"}
            : qq{$head"} . chr(123) . qq{loc:T } . localize($text, \$changed) . chr(125) . qq{"};
    !ge;

    next unless $changed;

    unless ($xaml =~ /xmlns:loc=/) {
        $xaml =~ s!(\n(\s*)xmlns:x="http://schemas\.microsoft\.com/winfx/2006/xaml")!$1\n$2xmlns:loc="clr-namespace:GetMan.Services"!;
    }

    open my $wh, '>:encoding(UTF-8)', $file or die "cannot write $file: $!";
    print $wh $xaml;
    close $wh;
    print "rewrote $file\n";
}

open my $jh, '>:encoding(UTF-8)', $out or die "cannot write $out: $!";
print $jh "{\n";
my @keys = sort keys %table;
for my $i (0 .. $#keys) {
    my $k = $keys[$i];
    my $v = $table{$k};
    $v =~ s/\\/\\\\/g;
    $v =~ s/"/\\"/g;
    $v =~ s/\n/\\n/g;
    print $jh qq{  "$k": "$v"};
    print $jh ($i == $#keys ? "\n" : ",\n");
}
print $jh "}\n";
close $jh;
print "wrote $out with " . scalar(@keys) . " strings\n";
